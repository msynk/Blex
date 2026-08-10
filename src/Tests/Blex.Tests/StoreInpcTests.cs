using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// XAML binding support: every store raises <see cref="INotifyPropertyChanged.PropertyChanged"/>
/// with an empty property name ("all properties changed") whenever <c>StateChanged</c> fires.
/// </summary>
public class StoreInpcTests
{
    private static List<string?> Track(BlexStoreBase store)
    {
        var raised = new List<string?>();
        ((INotifyPropertyChanged)store).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        return raised;
    }

    [Fact]
    public void Store_ImplementsINotifyPropertyChanged()
    {
        Assert.IsAssignableFrom<INotifyPropertyChanged>(new CounterStore());
    }

    [Fact]
    public void Action_WithMultipleMutations_RaisesOnce_WithEmptyName()
    {
        var store = new CounterStore();
        var raised = Track(store);

        store.Reset(); // no-op values, but Count/Label assignments are equal -> nothing dirty
        Assert.Empty(raised);

        store.Increment();
        Assert.Equal(new string?[] { string.Empty }, raised);

        raised.Clear();
        store.Add(2);
        store.Reset(); // mutates both Count and Label -> still a single batched raise
        Assert.Equal(new string?[] { string.Empty, string.Empty }, raised);
    }

    [Fact]
    public void StandaloneSet_Raises_AndEqualValueDoesNot()
    {
        var store = new CounterStore();
        var raised = Track(store);

        store.Count = 5;
        Assert.Single(raised);

        store.Count = 5; // unchanged value -> no notification
        Assert.Single(raised);
    }

    [Fact]
    public void Batch_RaisesOnce()
    {
        var store = new CounterStore();
        var raised = Track(store);

        store.Batch("Apply preset", () =>
        {
            store.Count = 10;
            store.Label = "preset";
        });

        Assert.Single(raised);
    }

    [Fact]
    public void RestoreState_Raises()
    {
        var store = new CounterStore();
        var snapshot = (JsonObject)store.SerializeState().DeepClone();
        store.Increment();

        var raised = Track(store);
        store.RestoreState(snapshot);

        Assert.Single(raised);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void PropertyChanged_StillRaised_WhenStateChangedSubscriberThrows()
    {
        var manager = new BlexManager();
        var errors = new List<BlexError>();
        manager.OnError = errors.Add;

        var store = new CounterStore();
        manager.Register(store);
        var raised = Track(store);
        store.StateChanged += () => throw new InvalidOperationException("boom");

        try
        {
            store.Increment();
        }
        catch (InvalidOperationException)
        {
            // Raw StateChanged subscribers are not isolated; the throw may propagate.
        }

        Assert.Single(raised); // ...but XAML bindings still observed the applied mutation.
        Assert.Equal(1, store.Count);
    }
}
