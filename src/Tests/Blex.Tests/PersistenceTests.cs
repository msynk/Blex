using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blex;
using Xunit;

namespace Blex.Tests;

public class PersistenceTests
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

    [Fact]
    public void PersistFlag_IsTrue_OnlyForOptedInStores()
    {
        Assert.True(new SettingsStore().Persist);
        Assert.False(new CounterStore().Persist);
    }

    [Fact]
    public async Task Persistor_SavesPersistentStore_AfterAction()
    {
        var storage = new InMemoryStorage();
        var manager = new BlexManager();
        var settings = new SettingsStore();
        manager.Register(settings);
        var persistor = new BlexStatePersistor(manager, storage);
        await persistor.StartAsync();

        settings.SetTheme("dark");

        Assert.True(storage.Data.ContainsKey("blex:settings"));
        Assert.Contains("dark", storage.Data["blex:settings"]);
    }

    [Fact]
    public async Task Persistor_DoesNotSave_NonPersistentStore()
    {
        var storage = new InMemoryStorage();
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        var persistor = new BlexStatePersistor(manager, storage);
        await persistor.StartAsync();

        counter.Increment();

        Assert.False(storage.Data.ContainsKey("blex:counter"));
    }

    [Fact]
    public async Task Persistor_RehydratesOnStart()
    {
        var storage = new InMemoryStorage();
        storage.Data["blex:settings"] = "{\"Theme\":\"dark\",\"FontSize\":20}";

        var manager = new BlexManager();
        var settings = new SettingsStore();
        manager.Register(settings);
        var persistor = new BlexStatePersistor(manager, storage);

        await persistor.StartAsync();

        Assert.Equal("dark", settings.Theme);
        Assert.Equal(20, settings.FontSize);
    }

    [Fact]
    public async Task Persistor_Restore_DoesNotRecordActions()
    {
        var storage = new InMemoryStorage();
        storage.Data["blex:settings"] = "{\"Theme\":\"dark\"}";

        var seen = new List<string>();
        var manager = new BlexManager(new[] { new BlexDelegateMiddleware(c => seen.Add(c.ActionName)) });
        var settings = new SettingsStore();
        manager.Register(settings);
        var persistor = new BlexStatePersistor(manager, storage);

        await persistor.StartAsync();

        Assert.Equal("dark", settings.Theme);
        Assert.Empty(seen); // restoration must not dispatch
    }

    [Fact]
    public async Task Persistor_Clear_RemovesPersistedSlice()
    {
        var storage = new InMemoryStorage();
        var manager = new BlexManager();
        var settings = new SettingsStore();
        manager.Register(settings);
        var persistor = new BlexStatePersistor(manager, storage);
        await persistor.StartAsync();

        settings.SetTheme("dark");
        Assert.True(storage.Data.ContainsKey("blex:settings"));

        await persistor.ClearAsync();
        Assert.False(storage.Data.ContainsKey("blex:settings"));
    }

    [Fact]
    public async Task Persistor_CustomKeyPrefix_IsUsed()
    {
        var storage = new InMemoryStorage();
        var manager = new BlexManager();
        var settings = new SettingsStore();
        manager.Register(settings);
        var persistor = new BlexStatePersistor(manager, storage, new BlexPersistenceOptions { KeyPrefix = "app." });
        await persistor.StartAsync();

        settings.SetTheme("dark");

        Assert.True(storage.Data.ContainsKey("app.settings"));
    }
}
