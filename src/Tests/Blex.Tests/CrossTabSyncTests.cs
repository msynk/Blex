using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// Cross-tab synchronization. Without it two open tabs quietly fight: each saves after every action,
/// so whichever acted last wins and the other's changes disappear on its next reload. With it, a
/// change made elsewhere is re-read and applied, and -- critically -- applying it does not write
/// back, or the same value would bounce between tabs forever.
/// </summary>
public class CrossTabSyncTests
{
    /// <summary>An in-memory storage that can also play the part of "another tab writing".</summary>
    private sealed class WatchableStorage : IStorageBlex, IExternalStorageChangeBlex
    {
        public Dictionary<string, string> Data { get; } = new();
        public int Writes { get; private set; }
        public int WatchStarts { get; private set; }
        public bool FailToStartWatching { get; set; }

        public event Action<string?>? ExternalChange;

        public ValueTask StartWatchingAsync(CancellationToken cancellationToken = default)
        {
            if (FailToStartWatching)
                throw new InvalidOperationException("interop unavailable");

            WatchStarts++;
            return ValueTask.CompletedTask;
        }

        /// <summary>Simulates another tab writing a value and the browser reporting it.</summary>
        public void WriteFromAnotherTab(string key, string value)
        {
            Data[key] = value;
            ExternalChange?.Invoke(key);
        }

        public void ClearFromAnotherTab()
        {
            Data.Clear();
            ExternalChange?.Invoke(null);
        }

        public bool HasSubscribers => ExternalChange is not null;

        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => new(Data.TryGetValue(key, out var v) ? v : null);

        public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Writes++;
            Data[key] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Data.Remove(key);
            return ValueTask.CompletedTask;
        }
    }

    private static (SettingsStore Store, ManagerBlex Manager, WatchableStorage Storage) Setup()
    {
        var store = new SettingsStore();
        var manager = new ManagerBlex();
        manager.Register(store);
        return (store, manager, new WatchableStorage());
    }

    private static PersistenceOptionsBlex Synced(Action<PersistenceOptionsBlex>? configure = null)
    {
        var options = new PersistenceOptionsBlex { SyncAcrossTabs = true };
        configure?.Invoke(options);
        return options;
    }

    [Fact]
    public async Task AChangeFromAnotherTab_IsApplied()
    {
        var (store, manager, storage) = Setup();
        await using var persistor = new StatePersistorBlex(manager, storage, Synced());
        await persistor.StartAsync();

        storage.WriteFromAnotherTab("blex:settings", """{"Theme":"dark","FontSize":18}""");
        await persistor.FlushAsync();

        Assert.Equal("dark", store.Theme);
        Assert.Equal(18, store.FontSize);
    }

    [Fact]
    public async Task ApplyingAChange_DoesNotWriteItBack()
    {
        var (_, manager, storage) = Setup();
        await using var persistor = new StatePersistorBlex(manager, storage, Synced());
        await persistor.StartAsync();
        var writesBefore = storage.Writes;

        storage.WriteFromAnotherTab("blex:settings", """{"Theme":"dark"}""");
        await persistor.FlushAsync();

        // Echoing the value back would bounce it between tabs forever.
        Assert.Equal(writesBefore, storage.Writes);
        Assert.Equal("""{"Theme":"dark"}""", storage.Data["blex:settings"]);
    }

    [Fact]
    public async Task ApplyingAChange_NotifiesSubscribers_ButRecordsNoAction()
    {
        var (store, manager, storage) = Setup();
        var recorded = new List<string>();
        using var sub = manager.Subscribe(ctx => recorded.Add(ctx.QualifiedName));
        var notifications = 0;
        store.StateChanged += () => notifications++;

        await using var persistor = new StatePersistorBlex(manager, storage, Synced());
        await persistor.StartAsync();

        storage.WriteFromAnotherTab("blex:settings", """{"Theme":"dark"}""");
        await persistor.FlushAsync();

        Assert.Equal(1, notifications); // the UI updates...
        Assert.Empty(recorded);         // ...but it is a restore, not an action
    }

    [Fact]
    public async Task OnExternalChange_ReportsTheStoreAndTheAppliedSlice()
    {
        var (_, manager, storage) = Setup();
        var seen = new List<(string Store, string Theme)>();
        var options = Synced(o => o.OnExternalChange = (name, state) =>
            seen.Add((name, state["Theme"]!.GetValue<string>())));

        await using var persistor = new StatePersistorBlex(manager, storage, options);
        await persistor.StartAsync();

        storage.WriteFromAnotherTab("blex:settings", """{"Theme":"dark"}""");
        await persistor.FlushAsync();

        Assert.Equal([("settings", "dark")], seen);
    }

    [Fact]
    public async Task AKeyBelongingToAnotherApp_IsIgnored()
    {
        var (store, manager, storage) = Setup();
        await using var persistor = new StatePersistorBlex(manager, storage, Synced());
        await persistor.StartAsync();

        storage.WriteFromAnotherTab("someOtherApp:token", "whatever");
        storage.WriteFromAnotherTab("blex:unknownStore", """{"Theme":"dark"}""");
        await persistor.FlushAsync();

        Assert.Equal("light", store.Theme);
    }

    [Fact]
    public async Task AWholesaleClear_ReReadsEveryStore_AndLeavesStateAloneWhenNothingIsStored()
    {
        var (store, manager, storage) = Setup();
        await using var persistor = new StatePersistorBlex(manager, storage, Synced());
        await persistor.StartAsync();
        store.SetTheme("dark");
        await persistor.FlushAsync();

        storage.ClearFromAnotherTab();
        await persistor.FlushAsync();

        // Nothing to apply, so the in-memory state stands rather than being wiped to defaults.
        Assert.Equal("dark", store.Theme);
    }

    [Fact]
    public async Task ACorruptPayloadFromAnotherTab_IsReportedAndSkipped()
    {
        var (store, manager, storage) = Setup();
        var errors = new List<ErrorBlex>();
        manager.OnError = errors.Add;

        await using var persistor = new StatePersistorBlex(manager, storage, Synced());
        await persistor.StartAsync();

        storage.WriteFromAnotherTab("blex:settings", "{ not json");
        await persistor.FlushAsync();

        Assert.Equal("light", store.Theme);
        Assert.Contains(errors, e => e.Source == "persistence");
    }

    [Fact]
    public async Task AnIncomingPayload_IsMigratedLikeAnyOther()
    {
        var (store, manager, storage) = Setup();
        var options = Synced(o =>
        {
            o.Version = 2;
            o.Migrate = (_, from, state) =>
            {
                if (from < 2)
                    state["Theme"] = "migrated";
                return state;
            };
        });

        await using var persistor = new StatePersistorBlex(manager, storage, options);
        await persistor.StartAsync();

        // A tab running an older build wrote a v1 payload.
        storage.WriteFromAnotherTab("blex:settings", """{"__blexVersion":1,"state":{"Theme":"dark"}}""");
        await persistor.FlushAsync();

        Assert.Equal("migrated", store.Theme);
    }

    [Fact]
    public async Task AThrowingOnExternalChange_IsContainedAndReported()
    {
        var (store, manager, storage) = Setup();
        var errors = new List<ErrorBlex>();
        manager.OnError = errors.Add;
        var options = Synced(o => o.OnExternalChange = (_, _) => throw new InvalidOperationException("boom"));

        await using var persistor = new StatePersistorBlex(manager, storage, options);
        await persistor.StartAsync();

        storage.WriteFromAnotherTab("blex:settings", """{"Theme":"dark"}""");
        await persistor.FlushAsync();

        Assert.Equal("dark", store.Theme); // the state still applied
        Assert.Contains(errors, e => e.Source == "persistence");
    }

    [Fact]
    public async Task WithoutSyncAcrossTabs_NothingIsWatched()
    {
        var (store, manager, storage) = Setup();
        await using var persistor = new StatePersistorBlex(manager, storage);
        await persistor.StartAsync();

        Assert.Equal(0, storage.WatchStarts);

        storage.WriteFromAnotherTab("blex:settings", """{"Theme":"dark"}""");
        await persistor.FlushAsync();

        Assert.Equal("light", store.Theme);
    }

    [Fact]
    public async Task Disposal_StopsListening()
    {
        var (store, manager, storage) = Setup();
        var persistor = new StatePersistorBlex(manager, storage, Synced());
        await persistor.StartAsync();
        await persistor.DisposeAsync();

        Assert.False(storage.HasSubscribers);

        storage.WriteFromAnotherTab("blex:settings", """{"Theme":"dark"}""");
        Assert.Equal("light", store.Theme);
    }

    [Fact]
    public async Task AFailedWatchSetup_LeavesNoDanglingSubscription_AndCanBeRetried()
    {
        var (store, manager, storage) = Setup();
        storage.FailToStartWatching = true;
        await using var persistor = new StatePersistorBlex(manager, storage, Synced());

        await Assert.ThrowsAsync<InvalidOperationException>(() => persistor.StartAsync());
        Assert.False(storage.HasSubscribers);

        // A retry once interop is available must actually take effect.
        storage.FailToStartWatching = false;
        await persistor.StartAsync();
        storage.WriteFromAnotherTab("blex:settings", """{"Theme":"dark"}""");
        await persistor.FlushAsync();

        Assert.Equal("dark", store.Theme);
    }

    [Fact]
    public async Task StartingTwice_WatchesOnce()
    {
        var (_, manager, storage) = Setup();
        await using var persistor = new StatePersistorBlex(manager, storage, Synced());

        await persistor.StartAsync();
        await persistor.StartAsync();

        Assert.Equal(1, storage.WatchStarts);
    }

    [Fact]
    public async Task AStorageWithoutTheInterface_IsSimplyNotWatched()
    {
        // SyncAcrossTabs on a storage that cannot report changes must be inert, not a failure.
        var store = new SettingsStore();
        var manager = new ManagerBlex();
        manager.Register(store);
        var plain = new PlainStorage();

        await using var persistor = new StatePersistorBlex(manager, plain, Synced());
        await persistor.StartAsync(); // must not throw

        store.SetTheme("dark");
        await persistor.FlushAsync();
        Assert.Contains("dark", plain.Data["blex:settings"]);
    }

    private sealed class PlainStorage : IStorageBlex
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
}
