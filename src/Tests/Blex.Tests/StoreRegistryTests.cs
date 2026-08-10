using System.Collections.Generic;
using System.Linq;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// The registry is copy-on-write, so a lazily-loaded feature that registers (or tears down) a store
/// from inside a notification cannot invalidate a walk already in progress.
/// </summary>
public class StoreRegistryTests
{
    [Fact]
    public void RegisteringFromInsideANotification_DoesNotInvalidateTheEnumeration()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);

        var added = false;
        counter.StateChanged += () =>
        {
            if (added)
                return;
            added = true;
            manager.Register(new SettingsStore()); // a lazily-loaded feature arriving mid-dispatch
        };

        // CaptureGlobalState walks the registry; the notification fires underneath it.
        counter.Increment();

        var state = manager.CaptureGlobalState();
        Assert.True(state.ContainsKey("counter"));
        Assert.True(state.ContainsKey("settings"));
    }

    [Fact]
    public void SnapshotTakenBeforeRegistration_IsUnaffectedByIt()
    {
        var manager = new BlexManager();
        manager.Register(new CounterStore());

        var snapshot = manager.Stores;
        manager.Register(new SettingsStore());

        Assert.Single(snapshot);            // the old view is frozen...
        Assert.Equal(2, manager.Stores.Count); // ...and the live one moved on
    }

    [Fact]
    public void SnapshotTakenBeforeUnregistration_IsUnaffectedByIt()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        var settings = new SettingsStore();
        manager.Register(counter);
        manager.Register(settings);

        var snapshot = manager.Stores;
        manager.Unregister(counter);

        Assert.Equal(2, snapshot.Count);
        Assert.Equal([settings], manager.Stores);
    }

    [Fact]
    public void Unregister_PreservesTheOrderOfTheRemainingStores()
    {
        var manager = new BlexManager();
        var a = new CounterStore();
        var b = new SettingsStore();
        var c = new ProfileStore();
        manager.Register(a);
        manager.Register(b);
        manager.Register(c);

        manager.Unregister(b);

        Assert.Equal(["counter", "profile"], manager.Stores.Select(s => s.Name));
    }

    [Fact]
    public void UnregisteringAnUnknownStore_IsANoOp()
    {
        var manager = new BlexManager();
        manager.Register(new CounterStore());

        manager.Unregister(new SettingsStore());

        Assert.Single(manager.Stores);
    }

    [Fact]
    public void GetStoreByName_MatchesTheGlobalStateKeys()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        manager.Register(new SettingsStore());

        Assert.Same(counter, manager.GetStore("counter"));
        Assert.Null(manager.GetStore("nope"));
        Assert.All(manager.CaptureGlobalState(), kvp => Assert.NotNull(manager.GetStore(kvp.Key)));
    }

    [Fact]
    public void GetStoreByName_ForgetsAnUnregisteredStore()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        manager.Unregister(counter);

        Assert.Null(manager.GetStore("counter"));
    }

    [Fact]
    public void UnregisteredStore_StopsBeingObserved_ButKeepsWorking()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        var seen = new List<string>();
        using var sub = manager.Subscribe(ctx => seen.Add(ctx.ActionName));

        counter.Increment();
        manager.Unregister(counter);
        counter.Increment();

        Assert.Equal(["Increment"], seen);
        Assert.Equal(2, counter.Count); // the store itself still works standalone
    }
}
