using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// State fields compare by reference, so an adapter operation that changes nothing must hand back
/// the very same <see cref="BlexEntityState{TEntity, TKey}"/> instance -- otherwise it raises a change
/// notification and records a time-travel action for a mutation that never happened.
/// </summary>
public class EntityNoOpTests
{
    private static readonly BlexEntityAdapter<Todo, int> Adapter = new(t => t.Id);

    private static BlexEntityState<Todo, int> Seeded()
        => Adapter.SetAll(Adapter.GetInitialState(), [new Todo(1, "a", false), new Todo(2, "b", false)]);

    [Fact]
    public void AddMany_WithNoNewEntities_ReturnsSameInstance()
    {
        var state = Seeded();
        Assert.Same(state, Adapter.AddMany(state, []));
        Assert.Same(state, Adapter.AddMany(state, [new Todo(1, "dupe", true)]));
    }

    [Fact]
    public void UpsertMany_WithNoEntities_ReturnsSameInstance()
    {
        var state = Seeded();
        Assert.Same(state, Adapter.UpsertMany(state, []));
        Assert.NotSame(state, Adapter.UpsertMany(state, [new Todo(1, "changed", true)]));
    }

    [Fact]
    public void RemoveMany_WithNoMatchingIds_ReturnsSameInstance()
    {
        var state = Seeded();
        Assert.Same(state, Adapter.RemoveMany(state, []));
        Assert.Same(state, Adapter.RemoveMany(state, [42, 43]));

        var removed = Adapter.RemoveMany(state, [1, 99]);
        Assert.NotSame(state, removed);
        Assert.Equal([2], removed.Ids);
    }

    [Fact]
    public void RemoveAll_OnEmptyState_ReturnsSameInstance()
    {
        var empty = Adapter.GetInitialState();
        Assert.Same(empty, Adapter.RemoveAll(empty));
        Assert.NotSame(Seeded(), Adapter.RemoveAll(Seeded()));
    }

    [Fact]
    public void SetAll_EmptyOnEmpty_ReturnsSameInstance()
    {
        var empty = Adapter.GetInitialState();
        Assert.Same(empty, Adapter.SetAll(empty, []));
    }

    [Fact]
    public void UpdateOne_ProducingAnEqualEntity_ReturnsSameInstance()
    {
        var state = Seeded();

        Assert.Same(state, Adapter.UpdateOne(state, 1, t => t));                       // same instance
        Assert.Same(state, Adapter.UpdateOne(state, 1, t => t with { Text = "a" }));   // equal record
        Assert.NotSame(state, Adapter.UpdateOne(state, 1, t => t with { Done = true }));
    }

    [Fact]
    public void Map_WithAnIdentityTransform_ReturnsSameInstance()
    {
        var state = Seeded();
        Assert.Same(state, Adapter.Map(state, t => t));
        Assert.NotSame(state, Adapter.Map(state, t => t with { Done = true }));
    }

    [Fact]
    public void NoOpAdapterCall_InsideAnAction_RecordsNothingAndDoesNotNotify()
    {
        var store = new EntityTodoStore();
        var manager = new BlexManager();
        manager.Register(store);
        var recorded = new List<string>();
        using var sub = manager.Subscribe(ctx => recorded.Add(ctx.ActionName));

        store.Add(new Todo(1, "a", false));
        recorded.Clear();

        var notifications = 0;
        store.StateChanged += () => notifications++;

        store.Remove(99);           // no such id
        store.Toggle(99);           // no such id

        Assert.Empty(recorded);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void NullArguments_ThrowArgumentNullException()
    {
        var state = Seeded();
        Assert.Throws<ArgumentNullException>(() => Adapter.AddMany(state, null!));
        Assert.Throws<ArgumentNullException>(() => Adapter.UpsertMany(state, null!));
        Assert.Throws<ArgumentNullException>(() => Adapter.RemoveMany(state, null!));
        Assert.Throws<ArgumentNullException>(() => Adapter.SetAll(state, null!));
    }

    [Fact]
    public void DeserializingAHalfWrittenPayload_DegradesToEmpty_InsteadOfThrowing()
    {
        // A persisted payload whose halves are missing/null must not produce an instance that
        // throws the moment a component enumerates it.
        var node = JsonNode.Parse("""{"ids":null,"entities":null}""")!;
        var state = JsonSerializer.Deserialize<BlexEntityState<Todo, int>>(node, BlexJson.Options)!;

        Assert.Equal(0, state.Count);
        Assert.Empty(state.All);
        Assert.False(state.Contains(1));
        Assert.Null(state.Find(1));
    }
}
