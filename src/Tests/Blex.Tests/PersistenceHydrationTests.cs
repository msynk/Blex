using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// Hydration status (<c>hasHydrated</c>/<c>onFinishHydration</c> in zustand terms) and the
/// <c>merge</c> hook that decides how a stored payload meets the live store.
/// </summary>
public class PersistenceHydrationTests
{
    private sealed class InMemoryStorage : IBlexStorage
    {
        public Dictionary<string, string> Data { get; } = new();

        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => new(Data.TryGetValue(key, out var v) ? v : null);

        public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Data[key] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Data.Remove(key);
            return ValueTask.CompletedTask;
        }
    }

    private static (BlexManager Manager, SettingsStore Store) Setup()
    {
        var manager = new BlexManager();
        var store = new SettingsStore();
        manager.Register(store);
        return (manager, store);
    }

    [Fact]
    public async Task HasHydrated_IsFalseUntilRehydrationCompletes()
    {
        var (manager, _) = Setup();
        var persistor = new BlexStatePersistor(manager, new InMemoryStorage());

        Assert.False(persistor.HasHydrated);
        await persistor.StartAsync();
        Assert.True(persistor.HasHydrated);
    }

    [Fact]
    public async Task HasHydrated_StaysFalse_WhenHydrationIsSkipped()
    {
        var (manager, _) = Setup();
        var options = new BlexPersistenceOptions { SkipHydration = true };
        var persistor = new BlexStatePersistor(manager, new InMemoryStorage(), options);

        await persistor.StartAsync();
        Assert.False(persistor.HasHydrated);

        await persistor.RehydrateAsync();
        Assert.True(persistor.HasHydrated);
    }

    [Fact]
    public async Task HydratedEvent_FiresOnceHydrationLands()
    {
        var (manager, _) = Setup();
        var persistor = new BlexStatePersistor(manager, new InMemoryStorage());

        var fired = 0;
        persistor.Hydrated += () => fired++;

        await persistor.StartAsync();
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task WhenHydratedAsync_CompletesOnHydration()
    {
        var (manager, store) = Setup();
        var storage = new InMemoryStorage();
        storage.Data["blex:settings"] = """{"Theme":"dark","FontSize":18}""";

        var options = new BlexPersistenceOptions { SkipHydration = true };
        var persistor = new BlexStatePersistor(manager, storage, options);
        await persistor.StartAsync();

        var waiting = persistor.WhenHydratedAsync();
        Assert.False(waiting.IsCompleted);

        await persistor.RehydrateAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("dark", store.Theme);
    }

    [Fact]
    public async Task WhenHydratedAsync_IsAlreadyCompleteAfterHydration()
    {
        var (manager, _) = Setup();
        var persistor = new BlexStatePersistor(manager, new InMemoryStorage());
        await persistor.StartAsync();

        Assert.True(persistor.WhenHydratedAsync().IsCompleted);
    }

    [Fact]
    public async Task WhenHydratedAsync_IsReleasedOnDisposal()
    {
        var (manager, _) = Setup();
        var options = new BlexPersistenceOptions { SkipHydration = true };
        var persistor = new BlexStatePersistor(manager, new InMemoryStorage(), options);
        await persistor.StartAsync();

        var waiting = persistor.WhenHydratedAsync();
        await persistor.DisposeAsync();

        // A persistor torn down before it hydrated must not leave callers awaiting forever; the
        // flag still reports that hydration never happened.
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(persistor.HasHydrated);
    }

    [Fact]
    public async Task Merge_CanKeepALiveValueThePayloadWouldOverwrite()
    {
        var (manager, store) = Setup();
        store.SetTheme("live");

        var storage = new InMemoryStorage();
        storage.Data["blex:settings"] = """{"Theme":"stored","FontSize":22}""";

        var options = new BlexPersistenceOptions
        {
            Merge = (name, persisted, current) =>
            {
                Assert.Equal("settings", name);
                persisted["Theme"] = current["Theme"]?.DeepClone();
                return persisted;
            },
        };

        var persistor = new BlexStatePersistor(manager, storage, options);
        await persistor.StartAsync();

        Assert.Equal("live", store.Theme);   // the live value won
        Assert.Equal(22, store.FontSize);    // the stored value applied
    }

    [Fact]
    public async Task Merge_ReturningNull_DiscardsThePayload()
    {
        var (manager, store) = Setup();
        var storage = new InMemoryStorage();
        storage.Data["blex:settings"] = """{"Theme":"stored"}""";

        var reported = new List<string?>();
        var options = new BlexPersistenceOptions
        {
            Merge = (_, _, _) => null,
            OnRehydrated = (name, state) => reported.Add(state is null ? null : name),
        };

        var persistor = new BlexStatePersistor(manager, storage, options);
        await persistor.StartAsync();

        Assert.Equal("light", store.Theme);
        Assert.Equal(new string?[] { null }, reported);
    }

    [Fact]
    public async Task Merge_MayReturnANestedNodeOfItsArguments()
    {
        var (manager, store) = Setup();
        var storage = new InMemoryStorage();
        storage.Data["blex:settings"] = """{"Nested":{"Theme":"dark","FontSize":30}}""";

        var options = new BlexPersistenceOptions
        {
            // Handing back a still-parented node must not throw: a JsonNode cannot be parented twice.
            Merge = (_, persisted, _) => persisted["Nested"] as JsonObject,
        };

        var persistor = new BlexStatePersistor(manager, storage, options);
        await persistor.StartAsync();

        Assert.Equal("dark", store.Theme);
        Assert.Equal(30, store.FontSize);
    }

    [Fact]
    public async Task AThrowingMerge_DiscardsThePayloadAndReports()
    {
        var (manager, store) = Setup();
        var errors = new List<BlexError>();
        manager.OnError = errors.Add;

        var storage = new InMemoryStorage();
        storage.Data["blex:settings"] = """{"Theme":"stored"}""";

        var options = new BlexPersistenceOptions { Merge = (_, _, _) => throw new InvalidOperationException("merge-boom") };
        var persistor = new BlexStatePersistor(manager, storage, options);
        await persistor.StartAsync();

        Assert.Equal("light", store.Theme);
        Assert.Single(errors);
        Assert.Equal("persistence", errors[0].Source);
    }
}
