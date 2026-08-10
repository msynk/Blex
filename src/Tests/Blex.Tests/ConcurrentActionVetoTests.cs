using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// A veto filter guards every action, including one dispatched while another asynchronous action of
/// the same store is still awaiting. <c>BlexEffectConcurrency.Parallel</c> is the default, so overlapping
/// effects are the common case rather than an exotic one.
/// </summary>
public class ConcurrentActionVetoTests
{
    [Fact]
    public async Task VetoApplies_ToAnActionStartedWhileAnotherIsInFlight()
    {
        var seen = new List<string>();
        var manager = new BlexManager([new BlexFilterMiddleware(ctx =>
        {
            seen.Add(ctx.ActionName);
            return ctx.ActionName != "Quick";
        })]);

        var store = new GatedStore();
        manager.Register(store);

        var slow = store.Slow();           // starts and parks on the gate
        await store.Quick();               // dispatched while Slow is still in flight

        store.Gate.SetResult();
        await slow;

        Assert.Contains("Slow", seen);
        Assert.Contains("Quick", seen);    // the filter was consulted...
        Assert.Equal(1, store.Count);      // ...and vetoed Quick's mutation (10 was not added)
    }

    [Fact]
    public async Task OverlappingActions_AreEachOfferedToTheFilter()
    {
        var seen = new List<string>();
        var manager = new BlexManager([new BlexFilterMiddleware(ctx => { seen.Add(ctx.ActionName); return true; })]);

        var store = new GatedStore();
        manager.Register(store);

        var slow = store.Slow();
        await store.Quick();
        store.Gate.SetResult();
        await slow;

        Assert.Equal(new[] { "Slow", "Quick" }, seen);
        Assert.Equal(11, store.Count);
    }

    [Fact]
    public async Task AsyncActionStartedInsideASyncBatch_InheritsTheBatchsDecision()
    {
        // The batch already passed the filters; a mutation it starts is part of it and must not
        // be re-offered (nor separately vetoable).
        var seen = new List<string>();
        var manager = new BlexManager([new BlexFilterMiddleware(ctx => { seen.Add(ctx.ActionName); return true; })]);

        var store = new GatedStore();
        manager.Register(store);

        Task quick = Task.CompletedTask;
        store.Batch("Preset", () => quick = store.Quick());
        await quick;

        Assert.Equal(new[] { "Preset" }, seen);
        Assert.Equal(10, store.Count);
    }

    [Fact]
    public async Task VetoedAction_RecordsNothingAndLeavesStateAlone()
    {
        var manager = new BlexManager([new BlexFilterMiddleware(ctx => ctx.ActionName != "Quick")]);
        var store = new GatedStore();
        manager.Register(store);

        var recorded = new List<string>();
        using var sub = manager.Subscribe(ctx => recorded.Add(ctx.ActionName));

        var slow = store.Slow();
        await store.Quick();
        store.Gate.SetResult();
        await slow;

        Assert.Equal(1, store.Count);
        Assert.DoesNotContain("Quick", recorded);
        Assert.Contains("Slow", recorded);
    }

    [Fact]
    public async Task VetoApplies_ToASynchronousActionStartedWhileAnAsyncOneIsInFlight()
    {
        // Gating the veto on the record depth (rather than the synchronous nesting depth) let a
        // plain sync action slip past every filter for as long as any effect was awaiting.
        var seen = new List<string>();
        var manager = new BlexManager([new BlexFilterMiddleware(ctx =>
        {
            seen.Add(ctx.ActionName);
            return ctx.ActionName != "Bump";
        })]);

        var store = new GatedStore();
        manager.Register(store);

        var slow = store.Slow();   // parks on the gate
        store.Bump();              // synchronous, dispatched mid-await

        store.Gate.SetResult();
        await slow;

        Assert.Contains("Bump", seen);   // the filter was consulted...
        Assert.Equal(1, store.Count);    // ...and vetoed it (100 was not added)
    }

    [Fact]
    public async Task SyncActionStartedWhileAnAsyncOneIsInFlight_IsOfferedToTheFilter()
    {
        var seen = new List<string>();
        var manager = new BlexManager([new BlexFilterMiddleware(ctx => { seen.Add(ctx.ActionName); return true; })]);

        var store = new GatedStore();
        manager.Register(store);

        var slow = store.Slow();
        store.Bump();
        store.Gate.SetResult();
        await slow;

        Assert.Equal(new[] { "Slow", "Bump" }, seen);
        Assert.Equal(101, store.Count);
    }

    [Fact]
    public void SyncActionsNestedInsideABatch_AreVetoedOnlyOnce()
    {
        // The outer batch owns the veto decision; its nested sync mutations must not re-enter it.
        var seen = new List<string>();
        var manager = new BlexManager([new BlexFilterMiddleware(ctx => { seen.Add(ctx.ActionName); return true; })]);

        var store = new CounterStore();
        manager.Register(store);

        store.Batch("Preset", () =>
        {
            store.Increment();
            store.Count = 9;
        });

        Assert.Equal(new[] { "Preset" }, seen);
    }
}
