using Blex;
using Xunit;

namespace Blex.Tests;

public class HistoryTests
{
    private static (BlexManager, CounterStore, BlexHistory) Setup(int max = 100)
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        var history = new BlexHistory(manager, max);
        history.Start();
        return (manager, counter, history);
    }

    [Fact]
    public void Undo_RevertsToPreviousState()
    {
        var (_, counter, history) = Setup();

        counter.Increment(); // 1
        counter.Increment(); // 2
        Assert.Equal(2, counter.Count);

        history.Undo();
        Assert.Equal(1, counter.Count);

        history.Undo();
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public void Redo_ReappliesUndoneState()
    {
        var (_, counter, history) = Setup();
        counter.Increment(); // 1
        counter.Increment(); // 2

        history.Undo(); // 1
        history.Undo(); // 0
        Assert.Equal(0, counter.Count);

        history.Redo(); // 1
        Assert.Equal(1, counter.Count);
        history.Redo(); // 2
        Assert.Equal(2, counter.Count);
    }

    [Fact]
    public void CanUndo_CanRedo_ReflectAvailability()
    {
        var (_, counter, history) = Setup();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);

        counter.Increment();
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        history.Undo();
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void NewAction_ClearsRedoStack()
    {
        var (_, counter, history) = Setup();
        counter.Increment(); // 1
        counter.Increment(); // 2
        history.Undo();      // 1, redo has [2]
        Assert.True(history.CanRedo);

        counter.Add(10);     // new branch -> redo cleared
        Assert.False(history.CanRedo);
        Assert.Equal(11, counter.Count);
    }

    [Fact]
    public void Undo_DoesNotRecordNewActions()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        var history = new BlexHistory(manager);
        history.Start();

        counter.Increment();
        counter.Increment();
        history.Undo(); // restore should not create history entries

        // After one undo we are at count 1, with redo available and one undo left.
        Assert.True(history.CanRedo);
        Assert.True(history.CanUndo);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void UndoRedoLabels_AndCounts_TrackActions()
    {
        var (_, store, history) = Setup();

        Assert.Null(history.NextUndoLabel);
        Assert.Equal(0, history.UndoCount);

        store.Increment();
        store.Add(5);

        Assert.Equal(2, history.UndoCount);
        Assert.Equal("counter/Add", history.NextUndoLabel);
        Assert.Null(history.NextRedoLabel);

        history.Undo();
        Assert.Equal("counter/Increment", history.NextUndoLabel);
        Assert.Equal("counter/Add", history.NextRedoLabel);
        Assert.Equal(1, history.RedoCount);

        history.Redo();
        Assert.Equal("counter/Add", history.NextUndoLabel);
        Assert.Null(history.NextRedoLabel);
    }

    [Fact]
    public void ThrowingChangedHandler_DoesNotBreakObserversRegisteredAfterHistory()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);

        var history = new BlexHistory(manager);
        history.Start(); // subscribes to ActionDispatched first
        history.Changed += () => throw new System.InvalidOperationException("ui boom");

        var errors = new System.Collections.Generic.List<BlexError>();
        manager.OnError = errors.Add;

        // A later observer (like the persistor) must still be notified.
        var observed = 0;
        using var sub = manager.Subscribe(_ => observed++);

        counter.Increment(); // must not throw

        Assert.Equal(1, observed);
        Assert.Contains(errors, e => e.Source == "history");
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void MaxEntries_CapsUndoDepth()
    {
        var (_, counter, history) = Setup(max: 2);

        counter.Increment(); // 1
        counter.Increment(); // 2
        counter.Increment(); // 3 (oldest baseline dropped)

        history.Undo(); // 2
        history.Undo(); // 1
        Assert.Equal(1, counter.Count);
        Assert.False(history.CanUndo); // only 2 entries retained
    }

    [Fact]
    public void Changed_FiresOnRecordAndUndo()
    {
        var (_, counter, history) = Setup();
        var fires = 0;
        history.Changed += () => fires++;

        counter.Increment();
        Assert.Equal(1, fires);

        history.Undo();
        Assert.Equal(2, fires);
    }
}
