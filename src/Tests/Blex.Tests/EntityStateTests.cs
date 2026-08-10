using System.Collections.Generic;
using System.Linq;
using Blex;
using Xunit;

namespace Blex.Tests;

public class EntityStateTests
{
    private static readonly BlexEntityAdapter<Todo, int> Adapter = new(t => t.Id);

    [Fact]
    public void AddOne_AppendsEntity()
    {
        var state = Adapter.GetInitialState();
        state = Adapter.AddOne(state, new Todo(1, "a", false));
        state = Adapter.AddOne(state, new Todo(2, "b", false));

        Assert.Equal(2, state.Count);
        Assert.Equal(new[] { 1, 2 }, state.Ids.ToArray());
        Assert.Equal("a", state.Find(1)!.Text);
    }

    [Fact]
    public void AddOne_IgnoresDuplicateId()
    {
        var state = Adapter.GetInitialState();
        state = Adapter.AddOne(state, new Todo(1, "a", false));
        state = Adapter.AddOne(state, new Todo(1, "dupe", false));

        Assert.Equal(1, state.Count);
        Assert.Equal("a", state.Find(1)!.Text); // original kept
    }

    [Fact]
    public void UpsertOne_AddsOrReplaces()
    {
        var state = Adapter.GetInitialState();
        state = Adapter.UpsertOne(state, new Todo(1, "a", false));
        state = Adapter.UpsertOne(state, new Todo(1, "updated", true));

        Assert.Equal(1, state.Count);
        Assert.Equal("updated", state.Find(1)!.Text);
        Assert.True(state.Find(1)!.Done);
    }

    [Fact]
    public void UpdateOne_MutatesViaUpdater()
    {
        var state = Adapter.GetInitialState();
        state = Adapter.AddOne(state, new Todo(1, "a", false));
        state = Adapter.UpdateOne(state, 1, t => t with { Done = true });

        Assert.True(state.Find(1)!.Done);
    }

    [Fact]
    public void RemoveOne_RemovesEntityAndId()
    {
        var state = Adapter.GetInitialState();
        state = Adapter.AddMany(state, new[] { new Todo(1, "a", false), new Todo(2, "b", false) });
        state = Adapter.RemoveOne(state, 1);

        Assert.Equal(1, state.Count);
        Assert.False(state.Contains(1));
        Assert.Equal(new[] { 2 }, state.Ids.ToArray());
    }

    [Fact]
    public void SetAll_ReplacesEverything()
    {
        var state = Adapter.GetInitialState();
        state = Adapter.AddOne(state, new Todo(99, "old", false));
        state = Adapter.SetAll(state, new[] { new Todo(1, "a", false), new Todo(2, "b", false) });

        Assert.Equal(new[] { 1, 2 }, state.Ids.ToArray());
    }

    [Fact]
    public void UpdateMany_UpdatesAllMatchedIds()
    {
        var adapter = new BlexEntityAdapter<Todo, int>(t => t.Id);
        var state = adapter.SetAll(adapter.GetInitialState(),
            [new Todo(1, "a", false), new Todo(2, "b", false), new Todo(3, "c", false)]);

        state = adapter.UpdateMany(state, [1, 3, 99], t => t with { Done = true });

        Assert.True(state.Find(1)!.Done);
        Assert.False(state.Find(2)!.Done);
        Assert.True(state.Find(3)!.Done);
        Assert.Equal(3, state.Count); // unknown id ignored
    }

    [Fact]
    public void Map_TransformsEveryEntity()
    {
        var adapter = new BlexEntityAdapter<Todo, int>(t => t.Id);
        var state = adapter.SetAll(adapter.GetInitialState(),
            [new Todo(1, "a", false), new Todo(2, "b", true)]);

        state = adapter.Map(state, t => t with { Text = t.Text.ToUpperInvariant() });

        Assert.Equal("A", state.Find(1)!.Text);
        Assert.Equal("B", state.Find(2)!.Text);
    }

    [Fact]
    public void SortComparer_KeepsIdsSorted_AcrossOperations()
    {
        var adapter = new BlexEntityAdapter<Todo, int>(
            t => t.Id,
            Comparer<Todo>.Create((a, b) => string.CompareOrdinal(a.Text, b.Text)));

        var state = adapter.GetInitialState();
        state = adapter.AddOne(state, new Todo(1, "zebra", false));
        state = adapter.AddOne(state, new Todo(2, "apple", false));
        state = adapter.AddOne(state, new Todo(3, "mango", false));

        Assert.Equal([2, 3, 1], state.Ids); // apple, mango, zebra

        // Renaming an entity re-positions it.
        state = adapter.UpdateOne(state, 2, t => t with { Text = "watermelon" });
        Assert.Equal([3, 2, 1], state.Ids); // mango, watermelon, zebra
    }

    [Fact]
    public void UpdateOne_IdChangeOntoExistingId_MergesWithoutDuplicatingIds()
    {
        var state = Adapter.SetAll(Adapter.GetInitialState(),
            [new Todo(1, "one", false), new Todo(2, "two", false)]);

        // Rekey entity 1 to id 2: it must overwrite entity 2 and drop the stale slot.
        state = Adapter.UpdateOne(state, 1, t => t with { Id = 2 });

        Assert.Equal([2], state.Ids);
        Assert.Equal(1, state.Count);
        Assert.Equal("one", state.Find(2)!.Text);
    }

    [Fact]
    public void Mutations_ReturnNewInstances()
    {
        var state = Adapter.GetInitialState();
        var next = Adapter.AddOne(state, new Todo(1, "a", false));
        Assert.NotSame(state, next); // immutability => change detection works
    }

    [Fact]
    public void EntityStore_IntegratesWithActionsAndComputed()
    {
        var store = new EntityTodoStore();
        store.Add(new Todo(1, "a", false));
        store.Add(new Todo(2, "b", false));
        Assert.Equal(2, store.Remaining);

        store.Toggle(1);
        Assert.Equal(1, store.Remaining);

        store.Remove(2);
        Assert.Equal(1, store.Todos.Count);
    }

    [Fact]
    public void EntityStore_SerializesAndRoundTrips()
    {
        var store = new EntityTodoStore();
        store.Add(new Todo(1, "a", false));
        store.Add(new Todo(2, "b", true));

        var json = store.SerializeState();
        var restored = new EntityTodoStore();
        restored.DeserializeState(json);

        Assert.Equal(2, restored.Todos.Count);
        Assert.Equal(new[] { 1, 2 }, restored.Todos.Ids.ToArray());
        Assert.True(restored.Todos.Find(2)!.Done);
    }
}
