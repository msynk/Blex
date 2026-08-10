using System;
using System.Collections.Generic;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// The history filter (redux-undo's <c>filter</c>) decides which actions get their own undo step.
/// A filtered action still applies and still moves the present forward -- it simply does not become
/// a separate step, so the next <see cref="BlexHistory.Undo"/> reverts it together with the recorded
/// action before it. That distinction is what keeps keystroke-level noise out of an undo stack the
/// user thinks about in whole edits.
/// </summary>
public class HistoryFilterTests
{
    private static (BlexManager Manager, CounterStore Counter, BlexHistory History) Setup(
        Func<BlexActionContext, bool>? filter)
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        var history = new BlexHistory(manager, 100, filter);
        history.Start();
        return (manager, counter, history);
    }

    [Fact]
    public void FilteredActions_DoNotBecomeTheirOwnUndoStep()
    {
        // "Set X" is what a direct property assignment records -- typically per keystroke.
        var (_, counter, history) = Setup(ctx => !ctx.ActionName.StartsWith("Set ", StringComparison.Ordinal));

        counter.Increment(); // recorded  -> Count 1
        counter.Count = 5;   // filtered  -> Count 5
        counter.Count = 9;   // filtered  -> Count 9

        Assert.Equal(1, history.UndoCount);

        history.Undo();
        Assert.Equal(0, counter.Count); // back past the filtered writes *and* the increment
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void FilteredActions_StillAdvanceThePresent_SoUndoNeverRestoresAStateTheUserNeverSaw()
    {
        var (_, counter, history) = Setup(ctx => ctx.ActionName != "Add");

        counter.Increment(); // recorded -> 1
        counter.Add(10);     // filtered -> 11
        counter.Increment(); // recorded -> 12

        Assert.Equal(12, counter.Count);

        // The step pushed for the last increment must be the *post-filter* state (11), not the
        // stale pre-filter one (1).
        history.Undo();
        Assert.Equal(11, counter.Count);
    }

    [Fact]
    public void FilteredAction_KeepsTheLabelOfTheStepUndoActuallyReturnsTo()
    {
        var (_, counter, history) = Setup(ctx => ctx.ActionName != "Add");

        counter.Increment();
        Assert.Equal("counter/Increment", history.NextUndoLabel);

        counter.Add(3); // filtered: no new step, so the boundary is still the increment
        Assert.Equal("counter/Increment", history.NextUndoLabel);
    }

    [Fact]
    public void FilteredAction_StillInvalidatesRedo()
    {
        var (_, counter, history) = Setup(ctx => ctx.ActionName != "Add");

        counter.Increment();
        history.Undo();
        Assert.True(history.CanRedo);

        counter.Add(2); // the state moved forward again; the redo branch is gone
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void FilteringEverything_LeavesNothingToUndo_ButStateStillChanges()
    {
        var (_, counter, history) = Setup(_ => false);

        counter.Increment();
        counter.Increment();

        Assert.Equal(2, counter.Count);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void AThrowingFilter_FailsOpenAndIsReported()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        var errors = new List<BlexError>();
        manager.OnError = errors.Add;

        var history = new BlexHistory(manager, 100, _ => throw new InvalidOperationException("filter boom"));
        history.Start();

        counter.Increment(); // must not throw

        // Losing an undo step silently is worse than keeping an extra one, so the action is
        // recorded anyway and the failure is surfaced.
        Assert.True(history.CanUndo);
        Assert.Contains(errors, e => e.Source == "history");
    }

    [Fact]
    public void WithoutAFilter_EveryActionIsRecorded()
    {
        var (_, counter, history) = Setup(filter: null);

        counter.Increment();
        counter.Count = 7;

        Assert.Equal(2, history.UndoCount);
    }

    [Fact]
    public void Checkpoint_TurnsFilteredWorkIntoItsOwnUndoStep()
    {
        // Without a checkpoint, the whole "typed edit" would be swallowed by the next recorded
        // action, so undo would discard the edit and that action together.
        var (_, counter, history) = Setup(ctx => !ctx.ActionName.StartsWith("Set ", StringComparison.Ordinal));

        counter.Count = 5;   // filtered
        counter.Count = 9;   // filtered
        history.Checkpoint("Edit");
        counter.Increment(); // recorded -> 10

        Assert.Equal(2, history.UndoCount);

        history.Undo();
        Assert.Equal(9, counter.Count); // back to the checkpoint, not past the whole edit

        history.Undo();
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public void Checkpoint_LabelsTheBoundaryItCreates()
    {
        var (_, counter, history) = Setup(_ => false);

        counter.Count = 5;
        history.Checkpoint("Edit");

        Assert.Equal("Edit", history.NextUndoLabel);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void Checkpoint_DefaultsItsLabel()
    {
        var (_, counter, history) = Setup(_ => false);

        counter.Count = 5;
        history.Checkpoint();

        Assert.Equal("Checkpoint", history.NextUndoLabel);
    }

    [Fact]
    public void Checkpoint_OnUnchangedState_AddsNothing()
    {
        // A debounced "the user stopped typing" timer fires whether or not anything was typed;
        // a checkpoint there must not create an undo step that reverts to its own state.
        var (_, counter, history) = Setup(_ => false);

        history.Checkpoint();
        Assert.False(history.CanUndo);

        counter.Count = 5;
        history.Checkpoint();
        history.Checkpoint();
        history.Checkpoint();

        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void Checkpoint_InvalidatesRedo()
    {
        var (_, counter, history) = Setup(filter: null);

        counter.Increment();
        history.Undo();
        Assert.True(history.CanRedo);

        counter.Count = 7;
        history.Checkpoint();

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Checkpoint_RaisesChanged()
    {
        var (_, counter, history) = Setup(_ => false);
        counter.Count = 5;

        // Subscribed after the mutation: a filtered action still raises Changed (its state moved),
        // so this isolates what the checkpoint itself reports.
        var fires = 0;
        history.Changed += () => fires++;

        history.Checkpoint();
        Assert.Equal(1, fires);

        history.Checkpoint(); // no-op, so nothing to report
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Checkpoint_RespectsMaxEntries()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        var history = new BlexHistory(manager, 2, _ => false);
        history.Start();

        counter.Count = 1;
        history.Checkpoint();
        counter.Count = 2;
        history.Checkpoint();
        counter.Count = 3;
        history.Checkpoint();

        Assert.Equal(2, history.UndoCount);
    }

    [Fact]
    public void Checkpoint_BeforeStart_StillEstablishesABaseline()
    {
        var manager = new BlexManager();
        var counter = new CounterStore();
        manager.Register(counter);
        var history = new BlexHistory(manager, 100, _ => false);

        counter.Count = 5;
        history.Checkpoint(); // no present yet: nothing to push, but the baseline is taken
        counter.Count = 9;
        history.Checkpoint();

        history.Undo();
        Assert.Equal(5, counter.Count);
    }
}
