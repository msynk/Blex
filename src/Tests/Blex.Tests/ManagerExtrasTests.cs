using System;
using System.Collections.Generic;
using Xunit;

namespace Blex.Tests;

public class ManagerExtrasTests
{
    [Fact]
    public void OnError_ReceivesSubscriberFailures()
    {
        var store = new CounterStore();
        var manager = new BlexManager();
        manager.Register(store);
        var errors = new List<BlexError>();
        manager.OnError = errors.Add;

        using var sub = manager.Subscribe(_ => throw new InvalidOperationException("boom"));
        store.Increment();

        Assert.Equal(1, store.Count); // dispatch survived
        var error = Assert.Single(errors);
        Assert.Equal("subscriber", error.Source);
        Assert.Equal("boom", error.Exception.Message);
        Assert.Equal("counter/Increment", error.Detail);
    }

    [Fact]
    public void OnError_ReceivesMiddlewareFailures()
    {
        var errors = new List<BlexError>();
        var store = new CounterStore();
        var manager = new BlexManager([new BlexDelegateMiddleware(_ => throw new InvalidOperationException("mw"))])
        {
            OnError = errors.Add,
        };
        manager.Register(store);

        store.Increment();

        Assert.Equal(1, store.Count);
        var error = Assert.Single(errors);
        Assert.Equal("middleware", error.Source);
    }

    [Fact]
    public void ThrowingOnErrorHandler_DoesNotBreakDispatch()
    {
        var store = new CounterStore();
        var manager = new BlexManager();
        manager.Register(store);
        manager.OnError = _ => throw new InvalidOperationException("handler is broken too");

        using var sub = manager.Subscribe(_ => throw new InvalidOperationException("boom"));
        store.Increment();

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void StateRestored_IsRaisedAfterRestoreGlobalState()
    {
        var store = new CounterStore();
        var manager = new BlexManager();
        manager.Register(store);

        var before = manager.CaptureGlobalState();
        store.Increment();

        var raised = 0;
        manager.StateRestored += () => raised++;
        manager.RestoreGlobalState(before);

        Assert.Equal(1, raised);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Restore_WithCorruptSlice_SkipsStoreButRestoresOthers()
    {
        var counter = new CounterStore();
        var settings = new SettingsStore();
        var manager = new BlexManager();
        manager.Register(counter);
        manager.Register(settings);
        var errors = new List<BlexError>();
        manager.OnError = errors.Add;

        counter.Increment();
        settings.SetTheme("dark");

        var snapshot = manager.CaptureGlobalState();
        snapshot["counter"] = new System.Text.Json.Nodes.JsonObject
        {
            ["Count"] = "not-an-int",
        };

        manager.RestoreGlobalState(snapshot);

        Assert.Equal("dark", settings.Theme); // unaffected store restored fine
        var error = Assert.Single(errors);
        Assert.Equal("restore", error.Source);
    }

    [Fact]
    public void Unregister_RemovesStoreFromPipeline()
    {
        var store = new CounterStore();
        var manager = new BlexManager();
        manager.Register(store);

        manager.Unregister(store);

        Assert.Empty(manager.Stores);
        Assert.Null(manager.GetStore<CounterStore>());
    }

    [Fact]
    public void Unregister_DetachesStore_ActionsNoLongerObserved()
    {
        var store = new CounterStore();
        var manager = new BlexManager();
        manager.Register(store);
        var seen = new List<string>();
        using var sub = manager.Subscribe(ctx => seen.Add(ctx.ActionName));

        store.Increment();
        Assert.Single(seen);

        manager.Unregister(store);
        store.Increment(); // still works standalone...
        Assert.Equal(2, store.Count);
        Assert.Single(seen); // ...but is no longer observed
    }

    [Fact]
    public void InFlightSnapshot_IsFrozen_BeforeReactorDispatchesFollowUp()
    {
        var counter = new CounterStore();
        var settings = new SettingsStore();
        var manager = new BlexManager();
        manager.Register(counter);
        manager.Register(settings);

        // First subscriber reacts to the counter action by mutating another store.
        using var reactor = manager.SubscribeTo<CounterStore>(_ =>
        {
            if (settings.Theme == "light")
                settings.SetTheme("dark");
        });

        // Second subscriber (registered later) lazily reads the ORIGINAL action's snapshot.
        var snapshots = new List<(string Action, string Theme)>();
        using var observer = manager.Subscribe(ctx => snapshots.Add(
            (ctx.ActionName, ctx.GlobalState["settings"]!["Theme"]!.GetValue<string>())));

        counter.Increment();

        // The Increment snapshot must show the world as of Increment -- before the reactor's
        // follow-up SetTheme mutated it.
        var incrementSnapshot = snapshots.Find(s => s.Action == "Increment");
        Assert.Equal("light", incrementSnapshot.Theme);
        var themeSnapshot = snapshots.Find(s => s.Action == "SetTheme");
        Assert.Equal("dark", themeSnapshot.Theme);
    }

    [Fact]
    public void History_RefreshesPresent_AfterExternalRestore()
    {
        var store = new CounterStore();
        var manager = new BlexManager();
        manager.Register(store);
        using var history = new BlexHistory(manager);
        history.Start();

        store.Increment();                       // 1
        var snapshotAt1 = manager.CaptureGlobalState();
        store.Increment();                       // 2

        manager.RestoreGlobalState(snapshotAt1); // external jump (DevTools-style) back to 1
        store.Increment();                       // 2 again, from the jumped-to state

        history.Undo();

        // Undo must return to the state the user actually saw before the last action (1, the
        // post-jump present) -- not the stale pre-jump snapshot (2).
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void ThrowingSubscriber_DuringStandaloneSet_DoesNotLeakDirtyFlag()
    {
        var store = new CounterStore();
        var manager = new BlexManager();
        manager.Register(store);
        var log = new List<string>();
        using var sub = manager.Subscribe(ctx => log.Add(ctx.ActionName));

        void Throwing() => throw new InvalidOperationException("render boom");
        store.StateChanged += Throwing;
        try
        {
            store.Count = 42;
        }
        catch (InvalidOperationException)
        {
            // The subscriber's exception propagates to the caller; that's fine.
        }

        store.StateChanged -= Throwing;

        // The set itself must still have been recorded (the mutation applied)...
        Assert.Contains("Set Count", log);

        // ...and a later no-op batch must NOT be recorded as a phantom action.
        store.Batch("Noop", () => { });
        Assert.DoesNotContain("Noop", log);
    }

    [Fact]
    public void RestoreFromInsideReactor_DoesNotCorruptInFlightSnapshots()
    {
        var store = new CounterStore();
        var manager = new BlexManager();
        manager.Register(store);
        var baseline = manager.CaptureGlobalState(); // Count = 0

        // Reactor 1 (subscribed first) undoes everything by restoring the baseline.
        using var reactor = manager.SubscribeToAction("Add", _ => manager.RestoreGlobalState(baseline));

        // Observer 2 (subscribed later) lazily reads the ORIGINAL action's snapshot.
        var counts = new List<int>();
        using var observer = manager.Subscribe(ctx => counts.Add(
            ctx.GlobalState["counter"]!["Count"]!.GetValue<int>()));

        store.Add(7);

        // The Add snapshot must show 7 (frozen before the reactor's restore), even though the
        // live store is back at 0.
        Assert.Equal([7], counts);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void MalformedDevToolsMessage_DoesNotThrow()
    {
        var manager = new BlexManager();
        manager.Register(new CounterStore());

        manager.HandleDevToolsMessage("""{"type":"DISPATCH","payload":{"type":"JUMP_TO_STATE"},"state":"{corrupt"}""");
        manager.HandleDevToolsMessage("not json at all");
    }
}
