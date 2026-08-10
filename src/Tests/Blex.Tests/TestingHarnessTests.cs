using System.Threading.Tasks;
using Blex;
using Blex.Testing;
using Xunit;

namespace Blex.Tests;

public class TestingHarnessTests
{
    [Fact]
    public void Harness_RecordsActions()
    {
        using var harness = BlexTestHarness.For<CounterStore>();

        harness.Store.Increment();
        harness.Store.Add(5);

        Assert.Equal(2, harness.Log.Count);
        Assert.Equal(new[] { "Increment", "Add" }, harness.Log.Names);
        Assert.Equal("Add", harness.Log.Last!.ActionName);
    }

    [Fact]
    public void Harness_TracksQualifiedNamesAndCounts()
    {
        using var harness = BlexTestHarness.For<CounterStore>();

        harness.Store.Increment();
        harness.Store.Increment();

        Assert.True(harness.Log.Contains("counter/Increment"));
        Assert.Equal(2, harness.Log.CountOf("Increment"));
    }

    [Fact]
    public void Harness_SnapshotReflectsState()
    {
        using var harness = BlexTestHarness.For<CounterStore>();
        harness.Store.Add(7);

        var snap = harness.Snapshot();
        Assert.Equal(7, snap["Count"]!.GetValue<int>());
        Assert.Equal(7, harness.State["counter"]!["Count"]!.GetValue<int>());
    }

    [Fact]
    public async Task Harness_RecordsAsyncEffectAsSingleAction()
    {
        using var harness = BlexTestHarness.For<DataStore>();

        await harness.Store.Load("hi");

        Assert.Equal(new[] { "Load" }, harness.Log.Names);
    }

    [Fact]
    public void RecordActions_Extension_WorksOnManager()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        using var log = manager.RecordActions();

        counter.Increment();

        Assert.Equal(1, log.Count);
    }

    [Fact]
    public void CountNotifications_AssertsBatching()
    {
        var store = new CounterStore();
        store.Count = 5;
        store.Label = "x";
        var notifications = store.CountNotifications(() => store.Reset()); // resets two fields
        Assert.Equal(1, notifications); // batched into one
    }

    [Fact]
    public void Snapshot_Extension_ReturnsState()
    {
        var store = new CounterStore();
        store.Add(3);
        Assert.Equal(3, store.Snapshot()["Count"]!.GetValue<int>());
    }
}
