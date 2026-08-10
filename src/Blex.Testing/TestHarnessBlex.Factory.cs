using System.Text.Json.Nodes;

namespace Blex.Testing;

/// <summary>Factory helpers for <see cref="TestHarnessBlex{TStore}"/>.</summary>
public static class TestHarnessBlex
{
    /// <summary>Creates a harness for an already-constructed store.</summary>
    public static TestHarnessBlex<TStore> For<TStore>(TStore store) where TStore : StoreBaseBlex
        => new(store);

    /// <summary>Creates a harness for a store with a parameterless constructor.</summary>
    public static TestHarnessBlex<TStore> For<TStore>() where TStore : StoreBaseBlex, new()
        => new(new TStore());

    /// <summary>
    /// Creates a harness whose manager runs the given middleware, for testing a store together with
    /// the guard rails that wrap it in the real app -- a veto filter, an audit logger, a metrics
    /// recorder.
    /// </summary>
    /// <remarks>
    /// Without this, asserting that a filter actually blocks an action means abandoning the harness
    /// and hand-wiring a manager, which is exactly the setup the harness exists to remove. Named
    /// rather than overloading <c>For</c> so that <c>For&lt;TStore&gt;()</c> stays unambiguous.
    /// </remarks>
    /// <example><code>
    /// using var harness = TestHarnessBlex.WithMiddleware&lt;CartStore&gt;(
    ///     new FilterMiddlewareBlex(ctx => ctx.ActionName != "Checkout"));
    /// </code></example>
    public static TestHarnessBlex<TStore> WithMiddleware<TStore>(params IMiddlewareBlex[] middleware)
        where TStore : StoreBaseBlex, new()
        => new(new TStore(), middleware);

    /// <summary>
    /// Creates a harness for an already-constructed store whose manager runs the given middleware.
    /// </summary>
    public static TestHarnessBlex<TStore> WithMiddleware<TStore>(TStore store, params IMiddlewareBlex[] middleware)
        where TStore : StoreBaseBlex
        => new(store, middleware);

    /// <summary>
    /// Creates a harness whose manager vetoes any action the predicate rejects -- the shorthand for
    /// the overwhelmingly common single-filter case.
    /// </summary>
    /// <example><code>
    /// using var harness = TestHarnessBlex.WithFilter&lt;CartStore&gt;(ctx => ctx.ActionName != "Checkout");
    /// </code></example>
    public static TestHarnessBlex<TStore> WithFilter<TStore>(Func<PreActionContextBlex, bool> filter)
        where TStore : StoreBaseBlex, new()
        => new(new TStore(), [new FilterMiddlewareBlex(filter)]);
}
