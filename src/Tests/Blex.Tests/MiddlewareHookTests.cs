using System.Collections.Generic;
using System.Threading.Tasks;
using Blex;
using Xunit;

namespace Blex.Tests;

public class MiddlewareHookTests
{
    private sealed class RecordingMiddleware : IBlexMiddleware
    {
        public List<string> Before { get; } = new();
        public List<string> After { get; } = new();
        public System.Func<BlexPreActionContext, bool>? Veto { get; set; }

        public void BeforeAction(BlexPreActionContext context)
        {
            Before.Add(context.ActionName);
            if (Veto is not null && !Veto(context))
                context.Cancel();
        }

        public void OnAction(BlexActionContext context) => After.Add(context.ActionName);
    }

    [Fact]
    public void BeforeAction_FiresBeforeApply_ForDispatch()
    {
        var mw = new RecordingMiddleware();
        var manager = new BlexManager(new[] { mw });
        var counter = new CounterStore();
        manager.Register(counter);

        counter.Increment();

        Assert.Equal(new[] { "Increment" }, mw.Before);
        Assert.Equal(new[] { "Increment" }, mw.After);
    }

    [Fact]
    public void BeforeAction_Cancel_VetoesMutation_AndRecord()
    {
        var mw = new RecordingMiddleware { Veto = _ => false }; // veto everything
        var manager = new BlexManager(new[] { mw });
        var counter = new CounterStore();
        manager.Register(counter);

        counter.Increment();

        Assert.Equal(0, counter.Count);          // mutation never ran
        Assert.Empty(mw.After);                  // nothing recorded
        Assert.Equal(new[] { "Increment" }, mw.Before);
    }

    [Fact]
    public void BeforeAction_Cancel_VetoesStandaloneSet()
    {
        var mw = new RecordingMiddleware { Veto = c => c.ActionName != "Set Count" };
        var manager = new BlexManager(new[] { mw });
        var counter = new CounterStore();
        manager.Register(counter);

        counter.Count = 99;

        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task BeforeAction_Cancel_VetoesAsyncAction()
    {
        var mw = new RecordingMiddleware { Veto = _ => false };
        var manager = new BlexManager(new[] { mw });
        var counter = new CounterStore();
        manager.Register(counter);

        await counter.LoadData();

        Assert.Equal(0, counter.Count);  // async mutation never ran
        Assert.Equal("idle", counter.Label);
    }

    [Fact]
    public void FilterMiddleware_VetoesByPredicate()
    {
        var manager = new BlexManager(new[] { new BlexFilterMiddleware(c => c.ActionName != "Increment") });
        var counter = new CounterStore();
        manager.Register(counter);

        counter.Increment(); // vetoed
        counter.Add(5);      // allowed

        Assert.Equal(5, counter.Count);
    }

    [Fact]
    public void Veto_DoesNotNotifySubscribers()
    {
        var manager = new BlexManager(new[] { new BlexFilterMiddleware(_ => false) });
        var counter = new CounterStore();
        manager.Register(counter);
        var raised = 0;
        counter.StateChanged += () => raised++;

        counter.Increment();

        Assert.Equal(0, raised);
    }
}
