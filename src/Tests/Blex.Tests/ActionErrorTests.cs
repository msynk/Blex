using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// An action body that throws is reported to middleware and to
/// <see cref="BlexManager.ActionFailed"/> without being swallowed -- the counterpart to Pinia's
/// <c>$onAction</c> <c>onError</c> hook.
/// </summary>
public class ActionErrorTests
{
    private sealed class RecordingMiddleware : IBlexMiddleware
    {
        public List<BlexActionErrorContext> Errors { get; } = [];

        public void OnAction(BlexActionContext context)
        {
        }

        public void OnActionError(BlexActionErrorContext context) => Errors.Add(context);
    }

    [Fact]
    public void SyncActionThatThrows_IsReportedAndStillPropagates()
    {
        var middleware = new RecordingMiddleware();
        var manager = new BlexManager([middleware]);
        var store = new FailingStore();
        manager.Register(store);

        var seen = new List<BlexActionErrorContext>();
        manager.ActionFailed += seen.Add;

        Assert.Throws<InvalidOperationException>(store.Boom);

        Assert.Single(middleware.Errors);
        Assert.Equal("failing/Boom", middleware.Errors[0].QualifiedName);
        Assert.Equal("sync-boom", middleware.Errors[0].Exception.Message);
        Assert.Single(seen);
    }

    [Fact]
    public async Task AsyncActionThatThrows_IsReportedAndStillPropagates()
    {
        var middleware = new RecordingMiddleware();
        var manager = new BlexManager([middleware]);
        var store = new FailingStore();
        manager.Register(store);

        await Assert.ThrowsAsync<InvalidOperationException>(store.BoomAsync);

        Assert.Single(middleware.Errors);
        Assert.Equal("failing/BoomAsync", middleware.Errors[0].QualifiedName);
    }

    [Fact]
    public async Task EffectThatThrows_IsReportedWithItsArguments()
    {
        var middleware = new RecordingMiddleware();
        var manager = new BlexManager([middleware]);
        var store = new FailingStore();
        manager.Register(store);

        // The effect wrapper captures the failure into LoadError instead of propagating it, so the
        // hook is the only way an app-wide reporter learns about it.
        await store.Load("orders");

        Assert.NotNull(store.LoadError);
        Assert.Single(middleware.Errors);
        var reported = middleware.Errors[0];
        Assert.Equal("failing/Load", reported.QualifiedName);
        Assert.Equal("key", reported.Args[0].Name);
        Assert.Equal("orders", reported.Args[0].Value);
    }

    [Fact]
    public async Task CancellationIsNotAFailure()
    {
        var middleware = new RecordingMiddleware();
        var manager = new BlexManager([middleware]);
        var store = new FailingStore();
        manager.Register(store);

        var watch = store.Watch();
        store.CancelWatch();
        await watch;

        Assert.Empty(middleware.Errors);
        Assert.Null(store.WatchError);
    }

    [Fact]
    public void SuccessfulActions_ReportNothing()
    {
        var middleware = new RecordingMiddleware();
        var manager = new BlexManager([middleware]);
        var store = new FailingStore();
        manager.Register(store);

        store.Set(3);

        Assert.Empty(middleware.Errors);
    }

    [Fact]
    public void ANestedFailure_IsReportedOnceByTheActionThatThrew()
    {
        var middleware = new RecordingMiddleware();
        var manager = new BlexManager([middleware]);
        var store = new FailingStore();
        manager.Register(store);

        // The same exception passes through Outer's frame on its way out; reporting per frame would
        // multiply every nested failure.
        Assert.Throws<InvalidOperationException>(store.Outer);

        Assert.Single(middleware.Errors);
        Assert.Equal("failing/Boom", middleware.Errors[0].QualifiedName);
    }

    [Fact]
    public async Task ANestedFailureInsideAnEffect_IsAlsoReportedOnce()
    {
        var middleware = new RecordingMiddleware();
        var manager = new BlexManager([middleware]);
        var store = new FailingStore();
        manager.Register(store);

        await store.OuterEffect();

        Assert.NotNull(store.OuterEffectError);
        Assert.Single(middleware.Errors);
        Assert.Equal("failing/Boom", middleware.Errors[0].QualifiedName);
    }

    [Fact]
    public void DistinctFailures_AreEachReported()
    {
        var middleware = new RecordingMiddleware();
        var manager = new BlexManager([middleware]);
        var store = new FailingStore();
        manager.Register(store);

        Assert.Throws<InvalidOperationException>(store.Boom);
        Assert.Throws<InvalidOperationException>(store.Boom);

        Assert.Equal(2, middleware.Errors.Count);
    }

    [Fact]
    public void TheHarnessRecordsFailures()
    {
        using var harness = Blex.Testing.BlexTestHarness.For<FailingStore>();

        Assert.Throws<InvalidOperationException>(harness.Store.Boom);

        Assert.Single(harness.Failures);
        Assert.Equal("failing/Boom", harness.Failures[0].QualifiedName);
    }

    [Fact]
    public void AThrowingErrorHandler_IsIsolatedAndReported()
    {
        var manager = new BlexManager();
        var store = new FailingStore();
        manager.Register(store);

        var errors = new List<BlexError>();
        manager.OnError = errors.Add;
        manager.ActionFailed += _ => throw new InvalidOperationException("handler-boom");

        // The original failure still reaches the caller; the handler's own failure is contained.
        Assert.Throws<InvalidOperationException>(store.Boom);
        Assert.Single(errors);
        Assert.Equal("subscriber", errors[0].Source);
    }
}
