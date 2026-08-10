using System.Threading.Tasks;
using Blex.Testing;
using Xunit;

namespace Blex.Tests;

public class BatchAndResetTests
{
    [Fact]
    public void Batch_GroupsAdHocMutations_IntoOneActionAndNotification()
    {
        using var harness = TestHarnessBlex.For<CounterStore>();
        var store = harness.Store;

        var notifications = store.CountNotifications(() =>
            store.Batch("Apply preset", () =>
            {
                store.Count = 10;
                store.Label = "preset";
            }));

        Assert.Equal(1, notifications);
        Assert.Equal(1, harness.Log.Count);
        Assert.Equal("Apply preset", harness.Log.Last!.ActionName);
        Assert.Equal(10, store.Count);
        Assert.Equal("preset", store.Label);
    }

    [Fact]
    public void Batch_IsVetoable_ByFilterMiddleware()
    {
        var store = new CounterStore();
        var manager = new ManagerBlex([new FilterMiddlewareBlex(ctx => ctx.ActionName != "Blocked")]);
        manager.Register(store);

        store.Batch("Blocked", () => store.Count = 99);

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void ResetState_RestoresInitialValues_AndRecordsAction()
    {
        using var harness = TestHarnessBlex.For<CounterStore>();
        var store = harness.Store;

        store.Add(41);
        store.Label = "changed";
        store.ResetState();

        Assert.Equal(0, store.Count);
        Assert.Equal("idle", store.Label);
        Assert.Equal("ResetState", harness.Log.Last!.ActionName);
    }

    [Fact]
    public void ResetState_InvalidatesComputedValues()
    {
        using var harness = TestHarnessBlex.For<CounterStore>();
        var store = harness.Store;

        store.Add(5);
        Assert.Equal(10, store.DoubleCount);

        store.ResetState();
        Assert.Equal(0, store.DoubleCount);
    }

    [Fact]
    public void ResetState_OnAnUnchangedStore_RecordsNothingAndDoesNotNotify()
    {
        using var harness = TestHarnessBlex.For<CounterStore>();
        var store = harness.Store;
        var notifications = 0;
        store.StateChanged += () => notifications++;

        store.ResetState();

        Assert.Equal(0, harness.Log.Count);
        Assert.Equal(0, notifications);

        // ...and it still works once the store has actually drifted.
        store.Add(3);
        store.ResetState();
        Assert.Equal(0, store.Count);
        Assert.Equal("ResetState", harness.Log.Last!.ActionName);
    }

    [Fact]
    public void ResetState_IsNoOp_WhenStoreWasNeverRegistered()
    {
        var store = new CounterStore();
        store.Increment();

        store.ResetState(); // no manager, no captured baseline -> nothing happens

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void RestoreState_PublicHydration_NotifiesAndInvalidatesComputed()
    {
        var store = new CounterStore();
        store.Increment();
        var snapshot = store.SerializeState();

        var fresh = new CounterStore();
        var notified = 0;
        fresh.StateChanged += () => notified++;
        fresh.RestoreState(snapshot);

        Assert.Equal(1, notified);
        Assert.Equal(1, fresh.Count);
        Assert.Equal(2, fresh.DoubleCount); // computed must not be stale
    }

    [Fact]
    public async Task NestedSyncAction_InsideAsyncAction_StillNotifies()
    {
        var store = new CounterStore();
        var notifications = 0;
        store.StateChanged += () => notifications++;

        // A sync [ActionAttributeBlex] invoked while an async action is awaiting must flush its own
        // notification; the async action's record wraps it, but the render must not be lost.
        var task = store.LoadData(); // async action: sets Label, awaits, sets Count/Label

        store.Increment(); // nested relative to the in-flight async action

        Assert.True(notifications >= 2, $"expected the nested action to notify, got {notifications}");
        await task;
    }
}
