using System.Collections.Generic;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// A manager-level projection must also re-evaluate after a state <em>restore</em> (undo/redo,
/// DevTools time-travel, an incoming cross-tab change). Those paths change state without
/// dispatching anything, so watching actions alone leaves the subscription reporting a value the
/// application no longer holds.
/// </summary>
public class ManagerSelectorRestoreTests
{
    [Fact]
    public void Selector_ReEvaluatesAfterUndo()
    {
        var manager = new BlexManager();
        var store = new CounterStore();
        manager.Register(store);
        var history = new BlexHistory(manager);
        history.Start();

        var seen = new List<int>();
        using var sub = manager.Subscribe(() => store.Count, seen.Add);

        store.Increment();
        store.Increment();
        history.Undo();

        Assert.Equal(new[] { 1, 2, 1 }, seen);
    }

    [Fact]
    public void Selector_ReEvaluatesAfterAManualRestore()
    {
        var manager = new BlexManager();
        var store = new CounterStore();
        manager.Register(store);

        store.Increment();
        var snapshot = manager.CaptureGlobalState();
        store.Increment();

        var transitions = new List<(int Previous, int Current)>();
        using var sub = manager.Subscribe(() => store.Count, (prev, curr) => transitions.Add((prev, curr)));

        manager.RestoreGlobalState(snapshot);

        Assert.Equal([(2, 1)], transitions);
    }

    [Fact]
    public void ARestoreThatChangesNothing_DoesNotFire()
    {
        var manager = new BlexManager();
        var store = new CounterStore();
        manager.Register(store);
        store.Increment();

        var fired = 0;
        using var sub = manager.Subscribe(() => store.Count, _ => fired++);

        manager.RestoreGlobalState(manager.CaptureGlobalState());

        Assert.Equal(0, fired);
    }

    [Fact]
    public void DisposingDetachesBothPaths()
    {
        var manager = new BlexManager();
        var store = new CounterStore();
        manager.Register(store);

        var fired = 0;
        var sub = manager.Subscribe(() => store.Count, _ => fired++);
        var snapshot = manager.CaptureGlobalState();

        store.Increment();
        Assert.Equal(1, fired);

        sub.Dispose();
        store.Increment();
        manager.RestoreGlobalState(snapshot);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void AThrowingSelectorOnRestore_IsIsolatedAndReported()
    {
        var manager = new BlexManager();
        var store = new CounterStore();
        manager.Register(store);

        var errors = new List<BlexError>();
        manager.OnError = errors.Add;

        var snapshot = manager.CaptureGlobalState();
        store.Increment();

        var restored = false;
        manager.StateRestored += () => restored = true;

        // Throws only once subscribed: the initial read establishes the baseline value.
        var armed = false;
        using var sub = manager.Subscribe<int>(
            () => armed ? throw new System.InvalidOperationException("selector-boom") : store.Count,
            _ => { });
        armed = true;

        manager.RestoreGlobalState(snapshot);

        Assert.True(restored);   // the handlers behind it still ran
        Assert.Contains(errors, e => e.Source == "subscriber");
    }
}
