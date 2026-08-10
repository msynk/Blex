using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Blex;
using Xunit;

namespace Blex.Tests;

public class StoreBehaviorTests
{
    [Fact]
    public void StateProperty_RaisesStateChanged()
    {
        var store = new CounterStore();
        var raised = 0;
        store.StateChanged += () => raised++;

        store.Count = 5;

        Assert.Equal(5, store.Count);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void StateProperty_NoChange_DoesNotNotify()
    {
        var store = new CounterStore();
        store.Count = 5;
        var raised = 0;
        store.StateChanged += () => raised++;

        store.Count = 5; // same value

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Action_MutatesState()
    {
        var store = new CounterStore();
        store.Increment();
        store.Add(10);
        Assert.Equal(11, store.Count);
    }

    [Fact]
    public void Action_BatchesMultipleMutations_IntoSingleNotification()
    {
        var store = new CounterStore();
        store.Count = 7;
        store.Label = "x";
        var raised = 0;
        store.StateChanged += () => raised++;

        store.Reset(); // sets Count and Label

        Assert.Equal(0, store.Count);
        Assert.Equal("idle", store.Label);
        Assert.Equal(1, raised); // batched into one
    }

    [Fact]
    public void Computed_ReflectsState()
    {
        var store = new CounterStore();
        store.Count = 6;
        Assert.Equal(12, store.DoubleCount);
    }

    [Fact]
    public void Computed_IsMemoized_UntilStateChanges()
    {
        var store = new CounterStore();
        _ = store.TrackedCount;
        _ = store.TrackedCount;
        _ = store.TrackedCount;
        Assert.Equal(1, store.ComputeCalls); // memoized

        store.Increment(); // invalidates
        _ = store.TrackedCount;
        Assert.Equal(2, store.ComputeCalls);
    }

    [Fact]
    public async Task AsyncAction_Works_AndUsesExplicitName()
    {
        var store = new CounterStore();
        await store.LoadData();
        Assert.Equal(42, store.Count);
        Assert.Equal("loaded", store.Label);
    }

    [Fact]
    public async Task AsyncAction_NotifiesDuringAwait_ForLoadingState()
    {
        var store = new CounterStore();
        var raised = 0;
        store.StateChanged += () => raised++;

        var task = store.LoadData(); // sets Label="loading" synchronously, then awaits
        Assert.Equal("loading", store.Label);
        Assert.True(raised >= 1); // UI was notified before completion

        await task;
        Assert.Equal("loaded", store.Label);
    }

    [Fact]
    public void StandaloneSetter_RecordsActionOnManager()
    {
        var seen = new List<string>();
        var manager = new BlexManager(new[] { new BlexDelegateMiddleware(c => seen.Add(c.ActionName)) });
        var store = new CounterStore();
        manager.Register(store);

        store.Count = 3; // direct property set, no Dispatch

        Assert.Equal(new[] { "Set Count" }, seen);
    }

    [Fact]
    public async Task AsyncAction_RecordsExactlyOneAction()
    {
        var seen = new List<string>();
        var manager = new BlexManager(new[] { new BlexDelegateMiddleware(c => seen.Add(c.ActionName)) });
        var store = new CounterStore();
        manager.Register(store);

        await store.LoadData(); // sets Label then Count across an await

        Assert.Equal(new[] { "LoadData" }, seen); // batched into a single recorded action
    }

    [Fact]
    public async Task ValueTaskAction_IsAwaited_NotFireAndForget()
    {
        var store = new CounterStore();

        await store.LoadValue(); // returns ValueTask<int>; wrapper exposes Task

        Assert.Equal(7, store.Count); // mutation completed because the action was awaited
    }

    [Fact]
    public async Task Action_ExplicitLabelWithSpaces_KeepsValidWrapperName_AndLabel()
    {
        var seen = new List<string>();
        var manager = new BlexManager(new[] { new BlexDelegateMiddleware(c => seen.Add(c.ActionName)) });
        var store = new CounterStore();
        manager.Register(store);

        await store.LoadValue(); // wrapper name derived from OnLoadValue -> LoadValue

        Assert.Equal(new[] { "Load Value" }, seen); // label honors the spaced display name
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrips()
    {
        var store = new CounterStore();
        store.Count = 99;
        store.Label = "saved";

        var json = store.SerializeState();
        var restored = new CounterStore();
        restored.DeserializeState(json);

        Assert.Equal(99, restored.Count);
        Assert.Equal("saved", restored.Label);
    }

    [Fact]
    public void StoreName_HonorsAttribute_AndDefaults()
    {
        Assert.Equal("counter", new CounterStore().Name);
        Assert.Equal("TodoStore", new TodoStore().Name);
    }

    [Fact]
    public void ComplexState_SerializesCorrectly()
    {
        var store = new TodoStore();
        store.AddItem("a");
        store.AddItem("b");

        var json = store.SerializeState();
        var restored = new TodoStore();
        restored.DeserializeState(json);

        Assert.Equal(new List<string> { "a", "b" }, restored.Items);
    }
}
