using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blex.Testing;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// <c>DebounceMilliseconds</c> gates an effect behind a quiet period. The property that makes it
/// worth generating rather than hand-rolling is that a superseded invocation leaves <em>no</em>
/// trace: it does not flip the loading flag, does not clear the error, is never offered to
/// middleware and records no action. Anything less produces a spinner that flickers once per
/// keystroke and a DevTools timeline full of searches that never happened.
/// </summary>
public class EffectDebounceTests
{
    /// <summary>
    /// Mirrors <c>DebounceMilliseconds</c> on <c>EffectConcurrencyStore</c>'s debounced effects.
    /// Several tests assert on state <em>inside</em> the quiet period, so the window has to stay
    /// wider than a scheduling hiccup on a loaded two-core CI runner - a value tuned to a developer
    /// machine turns those assertions into a race that only fails in CI.
    /// </summary>
    private const int Quiet = 250;

    /// <summary>Long enough for the gate to elapse, short enough to keep the suite fast.</summary>
    private static Task SettleAsync() => Task.Delay(Quiet * 4);

    [Fact]
    public async Task OnlyTheLastInvocationOfABurstRuns()
    {
        using var harness = BlexTestHarness.For<EffectConcurrencyStore>();
        var store = harness.Store;

        await Task.WhenAll(store.TypeAhead("a"), store.TypeAhead("ab"), store.TypeAhead("abc"));

        Assert.Equal(1, store.DebouncedBodyRuns);
        Assert.Equal("abc", store.Last);
        Assert.Equal(1, store.Completed);
    }

    [Fact]
    public async Task SupersededInvocationsRecordNoAction()
    {
        using var harness = BlexTestHarness.For<EffectConcurrencyStore>();
        var store = harness.Store;

        await Task.WhenAll(store.TypeAhead("a"), store.TypeAhead("ab"));

        // One TypeAhead, plus the "Set Last"/"Set Completed" writes its body performs.
        Assert.Single(harness.Log.Names, n => n == "TypeAhead");
    }

    [Fact]
    public async Task SupersededInvocationsAreNeverOfferedToMiddleware()
    {
        var seen = new List<string>();
        using var harness = BlexTestHarness.WithFilter<EffectConcurrencyStore>(ctx =>
        {
            seen.Add(ctx.ActionName);
            return true;
        });

        await Task.WhenAll(harness.Store.TypeAhead("a"), harness.Store.TypeAhead("ab"));

        Assert.Single(seen, n => n == "TypeAhead");
    }

    [Fact]
    public async Task LoadingStaysFalseWhileTheGateIsWaiting()
    {
        using var harness = BlexTestHarness.For<EffectConcurrencyStore>();
        var store = harness.Store;
        var gate = new TaskCompletionSource();
        store.Gates.Enqueue(gate);

        var run = store.TypeAhead("a");

        // Still inside the quiet period: nothing observable has happened yet, so a spinner bound to
        // IsLoading does not blink once per keystroke.
        Assert.False(store.TypeAheadIsLoading);

        await SettleAsync();
        Assert.True(store.TypeAheadIsLoading); // now the body is parked on its gate

        gate.SetResult();
        await run;
        Assert.False(store.TypeAheadIsLoading);
    }

    [Fact]
    public async Task ASupersededInvocationDoesNotClearAPreviousError()
    {
        using var harness = BlexTestHarness.For<EffectConcurrencyStore>();
        var store = harness.Store;

        await store.DebouncedBoom();
        Assert.NotNull(store.DebouncedBoomError);

        // Start a burst and supersede it immediately; the error must survive, because the invocation
        // that would have cleared it never ran.
        var first = store.DebouncedBoom();
        var second = store.DebouncedBoom();
        Assert.NotNull(store.DebouncedBoomError);

        await Task.WhenAll(first, second);
        Assert.Equal(2, store.DebouncedBodyRuns);
    }

    [Fact]
    public async Task InvocationsSpacedApartEachRun()
    {
        using var harness = BlexTestHarness.For<EffectConcurrencyStore>();
        var store = harness.Store;

        await store.TypeAhead("first");
        await store.TypeAhead("second");

        Assert.Equal(2, store.DebouncedBodyRuns);
        Assert.Equal("second", store.Last);
        Assert.Equal(2, store.Completed);
    }

    [Fact]
    public async Task AVetoIsStillHonoured_AfterTheGateElapses()
    {
        using var harness = BlexTestHarness.WithFilter<EffectConcurrencyStore>(
            ctx => ctx.ActionName != "TypeAhead");

        await harness.Store.TypeAhead("a");

        Assert.Equal(0, harness.Store.DebouncedBodyRuns);
        Assert.Equal("", harness.Store.Last);
    }

    [Fact]
    public async Task DebounceComposesWithLatest_CancellingARequestAlreadyInFlight()
    {
        using var harness = BlexTestHarness.For<EffectConcurrencyStore>();
        var store = harness.Store;
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        store.Gates.Enqueue(first);
        store.Gates.Enqueue(second);

        var older = store.LiveSearch("a");
        await SettleAsync();          // the gate elapsed, so this one is genuinely in flight
        Assert.True(store.LiveSearchIsLoading);

        var newer = store.LiveSearch("ab");
        await SettleAsync();          // ...and this one gets through, superseding it

        // The older request was cancelled rather than allowed to finish and write a stale result:
        // its gate is still unset, and awaiting it completes without ever running its body.
        await older;
        Assert.False(first.Task.IsCompleted);
        Assert.Equal("", store.Last);

        second.SetResult();
        await newer;

        Assert.Equal(2, store.DebouncedBodyRuns);
        Assert.Equal("ab", store.Last);
        Assert.Equal(1, store.Completed);   // only the surviving request wrote a result
        Assert.Null(store.LiveSearchError); // supersession is not a failure
    }

    [Fact]
    public async Task ANewBurstAfterCompletionStartsCleanly()
    {
        using var harness = BlexTestHarness.For<EffectConcurrencyStore>();
        var store = harness.Store;

        await Task.WhenAll(store.TypeAhead("a"), store.TypeAhead("ab"));
        await Task.WhenAll(store.TypeAhead("x"), store.TypeAhead("xy"));

        Assert.Equal(2, store.DebouncedBodyRuns);
        Assert.Equal("xy", store.Last);
    }

    [Fact]
    public async Task AThrowingBodyStillReportsThroughXxxError()
    {
        using var harness = BlexTestHarness.For<EffectConcurrencyStore>();

        await harness.Store.DebouncedBoom();

        Assert.IsType<InvalidOperationException>(harness.Store.DebouncedBoomError);
        Assert.False(harness.Store.DebouncedBoomIsLoading);
    }
}
