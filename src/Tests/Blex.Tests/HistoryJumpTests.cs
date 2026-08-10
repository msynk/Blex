using System;
using System.Linq;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// Multi-step history navigation (redux-undo's <c>jump</c>/<c>jumpToPast</c>/<c>jumpToFuture</c>)
/// and the labels a history panel renders.
/// </summary>
public class HistoryJumpTests
{
    private static (BlexManager Manager, CounterStore Store, BlexHistory History) Setup()
    {
        var manager = new BlexManager();
        var store = new CounterStore();
        manager.Register(store);
        var history = new BlexHistory(manager);
        history.Start();
        return (manager, store, history);
    }

    [Fact]
    public void Jump_MovesSeveralStepsBackAtOnce()
    {
        var (_, store, history) = Setup();
        store.Increment();
        store.Increment();
        store.Increment();
        Assert.Equal(3, store.Count);

        history.Jump(-2);

        Assert.Equal(1, store.Count);
        Assert.Equal(1, history.UndoCount);
        Assert.Equal(2, history.RedoCount);
    }

    [Fact]
    public void Jump_AppliesOnlyTheDestination()
    {
        var (_, store, history) = Setup();
        store.Increment();
        store.Increment();
        store.Increment();

        var renders = 0;
        store.StateChanged += () => renders++;
        history.Jump(-3);

        Assert.Equal(0, store.Count);
        Assert.Equal(1, renders);   // one restore, not three
    }

    [Fact]
    public void Jump_ForwardRedoesSeveralSteps()
    {
        var (_, store, history) = Setup();
        store.Increment();
        store.Increment();
        store.Increment();
        history.Jump(-3);

        history.Jump(2);

        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void Jump_IsClampedToWhatTheStacksHold()
    {
        var (_, store, history) = Setup();
        store.Increment();

        history.Jump(-99);
        Assert.Equal(0, store.Count);
        Assert.False(history.CanUndo);

        history.Jump(99);
        Assert.Equal(1, store.Count);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void JumpOfZero_OrOnEmptyStacks_DoesNothing()
    {
        var (_, store, history) = Setup();
        store.Increment();

        var changes = 0;
        history.Changed += () => changes++;

        history.Jump(0);
        history.Jump(5);   // nothing to redo

        Assert.Equal(0, changes);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void JumpToPast_IndexesTheOldestStateFirst()
    {
        var (_, store, history) = Setup();
        store.Increment();   // 1
        store.Increment();   // 2
        store.Increment();   // 3

        history.JumpToPast(0);   // the oldest retained state

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void JumpToFuture_IndexesTheNearestRedoFirst()
    {
        var (_, store, history) = Setup();
        store.Increment();
        store.Increment();
        store.Increment();
        history.JumpToPast(0);

        history.JumpToFuture(0);
        Assert.Equal(1, store.Count);

        history.JumpToFuture(1);
        Assert.Equal(3, store.Count);
    }

    [Fact]
    public void Labels_NameTheStepsInNavigationOrder()
    {
        var (_, store, history) = Setup();
        store.Increment();
        store.Add(5);
        store.Reset();

        Assert.Equal(new[] { "counter/Reset", "counter/Add", "counter/Increment" }, history.UndoLabels);
        Assert.Empty(history.RedoLabels);
        Assert.Equal("counter/Reset", history.NextUndoLabel);

        history.Undo();

        Assert.Equal(new[] { "counter/Add", "counter/Increment" }, history.UndoLabels);
        Assert.Equal(new[] { "counter/Reset" }, history.RedoLabels);
    }

    [Fact]
    public void Labels_AreEmptyBeforeAnythingHappens()
    {
        var (_, _, history) = Setup();
        Assert.Empty(history.UndoLabels);
        Assert.Empty(history.RedoLabels);
    }

    [Fact]
    public void Labels_AreCachedBetweenChanges()
    {
        var (_, store, history) = Setup();
        store.Increment();

        // Rendering a history panel reads these once per item; rebuilding on every access would
        // make the natural loop quadratic.
        Assert.Same(history.UndoLabels, history.UndoLabels);

        var before = history.UndoLabels;
        store.Increment();
        Assert.NotSame(before, history.UndoLabels);
        Assert.Equal(2, history.UndoLabels.Count);
    }

    [Fact]
    public void UndoLabels_MatchTheJumpToPastIndexTheyDocument()
    {
        var (_, store, history) = Setup();
        store.Increment();
        store.Add(5);
        store.Reset();

        // Item i is what JumpToPast(UndoCount - 1 - i) reverts: picking the last item must land on
        // the state before the very first action.
        var labels = history.UndoLabels;
        history.JumpToPast(history.UndoCount - 1 - (labels.Count - 1));

        Assert.Equal(0, store.Count);
        Assert.Equal("idle", store.Label);
    }
}
