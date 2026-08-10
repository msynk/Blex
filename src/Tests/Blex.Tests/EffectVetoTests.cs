using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// A veto means the action never happened. An effect's loading counter, error slot, cancellation
/// token and queue slot are all observable state, so a vetoed effect must not touch any of them.
/// </summary>
public class EffectVetoTests
{
    private static (EffectConcurrencyStore Store, BlexManager Manager) Setup(Func<BlexPreActionContext, bool> filter)
    {
        var manager = new BlexManager([new BlexFilterMiddleware(filter)]);
        var store = new EffectConcurrencyStore();
        manager.Register(store);
        return (store, manager);
    }

    [Fact]
    public async Task VetoedEffect_NeverReportsLoading()
    {
        var (store, _) = Setup(_ => false);

        var loadingSeen = false;
        store.StateChanged += () => loadingSeen |= store.FetchIsLoading;

        await store.Fetch();

        Assert.False(loadingSeen);
        Assert.False(store.FetchIsLoading);
    }

    [Fact]
    public async Task VetoedEffect_DoesNotClearAPreviousError()
    {
        var allow = true;
        var (store, _) = Setup(_ => allow);

        // Let one run fail so there is an error to preserve.
        store.Gates.Enqueue(new TaskCompletionSource());
        var failing = store.Timeout();
        await failing;
        Assert.NotNull(store.TimeoutError);
        var original = store.TimeoutError;

        allow = false;
        await store.Timeout(); // vetoed retry

        Assert.Same(original, store.TimeoutError);
    }

    [Fact]
    public async Task VetoedLatestEffect_DoesNotCancelTheRunInFlight()
    {
        var allow = true;
        var (store, _) = Setup(_ => allow);

        var gate = new TaskCompletionSource();
        store.Gates.Enqueue(gate);
        var inFlight = store.Search("first");

        allow = false;
        await store.Search("second"); // vetoed: must not supersede "first"

        gate.SetResult();
        await inFlight;

        Assert.Equal("first", store.Last);
        Assert.Equal(1, store.Completed);
        Assert.Null(store.SearchError);
    }

    [Fact]
    public async Task VetoedQueueEffect_DoesNotTakeASlot()
    {
        var allow = true;
        var (store, _) = Setup(_ => allow);

        var first = new TaskCompletionSource();
        var third = new TaskCompletionSource();
        store.Gates.Enqueue(first);
        store.Gates.Enqueue(third);

        var a = store.Write("a");

        allow = false;
        var vetoed = store.Write("b"); // must not queue behind "a"
        await vetoed;
        Assert.True(vetoed.IsCompleted);

        allow = true;
        var c = store.Write("c");

        first.SetResult();
        third.SetResult();
        await Task.WhenAll(a, c);

        Assert.Equal("ac", store.Last); // "b" never ran
        Assert.Equal(2, store.Completed);
    }

    [Fact]
    public async Task VetoedEffect_RecordsNothing()
    {
        var (store, manager) = Setup(ctx => ctx.ActionName != "Fetch");
        var recorded = new List<string>();
        using var sub = manager.Subscribe(ctx => recorded.Add(ctx.ActionName));

        await store.Fetch();

        Assert.Empty(recorded);
    }

    [Fact]
    public async Task AllowedEffect_StillRunsItsFullLifecycle()
    {
        var (store, manager) = Setup(_ => true);
        var recorded = new List<string>();
        using var sub = manager.Subscribe(ctx => recorded.Add(ctx.ActionName));

        var gate = new TaskCompletionSource();
        store.Gates.Enqueue(gate);
        var run = store.Fetch();

        Assert.True(store.FetchIsLoading);
        gate.SetResult();
        await run;

        Assert.False(store.FetchIsLoading);
        Assert.Equal(1, store.Completed);
        Assert.Equal(["Fetch"], recorded);
    }

    [Fact]
    public async Task DroppedEffect_NeverReachesTheFilter()
    {
        var seen = new List<string>();
        var manager = new BlexManager([new BlexFilterMiddleware(ctx => { seen.Add(ctx.ActionName); return true; })]);
        var store = new EffectConcurrencyStore();
        manager.Register(store);

        var gate = new TaskCompletionSource();
        store.Gates.Enqueue(gate);
        var first = store.Submit();
        await store.Submit(); // dropped: nothing was dispatched, so nothing to veto

        gate.SetResult();
        await first;

        Assert.Equal(["Submit"], seen);
    }

    [Fact]
    public async Task EffectArguments_ReachTheFilterBeforeTheBodyRuns()
    {
        List<BlexActionArg>? captured = null;
        var manager = new BlexManager([new BlexFilterMiddleware(ctx =>
        {
            captured = [.. ctx.Args];
            return false;
        })]);
        var store = new EffectConcurrencyStore();
        manager.Register(store);

        await store.Search("needle");

        Assert.NotNull(captured);
        var arg = Assert.Single(captured);
        Assert.Equal("query", arg.Name);
        Assert.Equal("needle", arg.Value);
        Assert.Equal("", store.Last); // body never ran
    }
}
