using System;
using System.Threading.Tasks;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// Disposing a store quiesces it: in-flight effects are cancelled and pending debounce timers stop,
/// so a scope torn down mid-request does not leave work running against it.
/// </summary>
public class StoreDisposalTests
{
    [Fact]
    public async Task Dispose_CancelsAnInFlightEffect()
    {
        var manager = new BlexManager();
        var store = new FailingStore();
        manager.Register(store);

        var watch = store.Watch();
        Assert.True(store.WatchIsLoading);

        store.Dispose();
        await watch.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(store.WatchIsLoading);
        Assert.Equal(0, store.Value);      // the body never got past the gate
        Assert.Null(store.WatchError);     // cancellation is a normal outcome, not a failure
    }

    [Fact]
    public async Task Dispose_StopsAPendingDebounce()
    {
        var manager = new BlexManager();
        var store = new FailingStore();
        manager.Register(store);

        var pending = store.SlowDebounce();   // parks for 10 s unless cancelled
        store.Dispose();

        // Without cancelling the debounce source this would sit out the full interval.
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, store.Value);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var store = new FailingStore();
        store.Dispose();
        store.Dispose();
    }

    [Fact]
    public void Dispose_LeavesTheStoreUsable()
    {
        var manager = new BlexManager();
        var store = new FailingStore();
        manager.Register(store);
        store.Dispose();

        // Disposal is a best-effort quiesce, not a hard kill: the store still serializes and still
        // dispatches, so teardown ordering cannot strand a final persistence flush.
        store.Set(5);
        Assert.Equal(5, store.Value);
        Assert.Equal(5, store.SerializeState()["Value"]!.GetValue<int>());
    }

    [Fact]
    public void AStoreWithoutCancellableEffects_DisposesCleanly()
    {
        var store = new CounterStore();
        store.Dispose();
        store.Increment();
        Assert.Equal(1, store.Count);
    }
}
