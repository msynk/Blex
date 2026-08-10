using System;
using System.Collections.Generic;
using System.Linq;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// Collection comparers for selector subscriptions -- the zustand <c>shallow</c> equivalent that
/// keeps a projection returning a fresh list from firing on every action.
/// </summary>
public class ComparerTests
{
    [Fact]
    public void Sequence_ComparesContentsInOrder()
    {
        var comparer = BlexComparers.Sequence<int>();

        Assert.True(comparer.Equals(new List<int> { 1, 2, 3 }, new[] { 1, 2, 3 }));
        Assert.False(comparer.Equals(new[] { 1, 2, 3 }, new[] { 3, 2, 1 }));
        Assert.False(comparer.Equals(new[] { 1, 2 }, new[] { 1, 2, 3 }));
    }

    [Fact]
    public void Sequence_TreatsNullsConsistently()
    {
        var comparer = BlexComparers.Sequence<int>();

        Assert.True(comparer.Equals(null, null));
        Assert.False(comparer.Equals(null, Array.Empty<int>()));
        Assert.Equal(0, comparer.GetHashCode(null!));
    }

    [Fact]
    public void Sequence_HashesEqualSequencesAlike()
    {
        var comparer = BlexComparers.Sequence<string>();
        Assert.Equal(comparer.GetHashCode(new[] { "a", "b" }), comparer.GetHashCode(new List<string> { "a", "b" }));
    }

    [Fact]
    public void Set_IgnoresOrderAndRepetition()
    {
        var comparer = BlexComparers.Set<int>();

        Assert.True(comparer.Equals(new[] { 1, 2, 3 }, new[] { 3, 1, 2 }));
        Assert.True(comparer.Equals(new[] { 1, 1, 2 }, new[] { 2, 1 }));
        Assert.False(comparer.Equals(new[] { 1, 2 }, new[] { 1, 3 }));
        Assert.Equal(comparer.GetHashCode(new[] { 1, 2 }), comparer.GetHashCode(new[] { 2, 1 }));
    }

    [Fact]
    public void Sequence_HonoursACustomElementComparer()
    {
        var comparer = BlexComparers.Sequence<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(comparer.Equals(new[] { "AB" }, new[] { "ab" }));
    }

    [Fact]
    public void ASelectorProjectingAFreshList_FiresOnlyOnRealChanges()
    {
        var manager = new BlexManager();
        var store = new EntityTodoStore();
        manager.Register(store);

        var fired = 0;
        using var sub = store.Subscribe(
            () => store.Todos.All.Where(t => !t.Done),
            _ => fired++,
            BlexComparers.Sequence<Todo>());

        store.Add(new Todo(1, "write", false));
        Assert.Equal(1, fired);

        // An unrelated action still re-runs the projection, which builds a new sequence with the
        // same contents -- without a content comparer this would count as a change.
        store.Add(new Todo(2, "ship", true));
        Assert.Equal(1, fired);

        store.Toggle(1);
        Assert.Equal(2, fired);
    }

    [Fact]
    public void WithoutAComparer_AFreshListLooksLikeAChangeEveryTime()
    {
        var manager = new BlexManager();
        var store = new EntityTodoStore();
        manager.Register(store);

        var fired = 0;
        using var sub = store.Subscribe(() => store.Todos.All.ToList(), _ => fired++);

        store.Add(new Todo(1, "write", false));
        store.Add(new Todo(2, "ship", true));

        Assert.Equal(2, fired);   // the baseline the comparers exist to fix
    }
}
