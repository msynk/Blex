namespace Blex.Demo.Stores;

/// <summary>
/// A realistic type-ahead: <c>Latest</c> concurrency means each keystroke supersedes the
/// in-flight request, and the trailing <see cref="CancellationToken"/> makes the generator emit
/// <c>CancelSearch()</c>. The <c>SearchIsLoading</c> / <c>SearchError</c> properties come for free.
/// </summary>
[StoreAttributeBlex(Name = "search")]
public partial class SearchStore
{
    private static readonly string[] Catalog =
    [
        "Blazor WebAssembly", "Blazor Server", "Blazor Hybrid", "Razor Components",
        "Source Generators", "Roslyn Analyzers", "Incremental Generators",
        "Redux DevTools", "Time-travel Debugging", "Entity Adapter",
        "State Management", "Dependency Injection", "Middleware Pipeline",
    ];

    [StateAttributeBlex] private string _query = "";
    [StateAttributeBlex] private IReadOnlyList<string> _results = [];
    [StateAttributeBlex] private bool _simulateFailure;

    [ComputedAttributeBlex] private bool ComputeHasResults() => Results.Count > 0;

    [ActionAttributeBlex] private void OnSetSimulateFailure(bool value) => SimulateFailure = value;

    [EffectAttributeBlex(Concurrency = EffectConcurrencyBlex.Latest)]
    private async Task OnSearch(string query, CancellationToken ct)
    {
        Query = query;

        // A deliberately slow "network" call so supersession and cancellation are observable.
        await Task.Delay(700, ct);

        if (SimulateFailure)
            throw new InvalidOperationException($"The search service is unavailable (simulated) for '{query}'.");

        Results = string.IsNullOrWhiteSpace(query)
            ? []
            : [.. Catalog.Where(c => c.Contains(query, StringComparison.OrdinalIgnoreCase))];
    }

    [ActionAttributeBlex]
    private void OnClear()
    {
        Query = "";
        Results = [];
        Keystrokes = 0;
        RequestsStarted = 0;
    }

    // ---- Debounced variant -------------------------------------------------------------------
    // Same search, gated behind a quiet period. Latest still matters: once a request does get
    // through, a later one that also gets through supersedes it.

    [StateAttributeBlex] private int _keystrokes;
    [StateAttributeBlex] private int _requestsStarted;

    [ActionAttributeBlex] private void OnCountKeystroke() => Keystrokes++;

    [EffectAttributeBlex(DebounceMilliseconds = 300, Concurrency = EffectConcurrencyBlex.Latest)]
    private async Task OnTypeAhead(string query, CancellationToken ct)
    {
        // Reached only by the invocation that survived the 300 ms gate, so this counter is the
        // honest count of requests the service would have seen.
        RequestsStarted++;
        Query = query;

        await Task.Delay(700, ct);

        if (SimulateFailure)
            throw new InvalidOperationException($"The search service is unavailable (simulated) for '{query}'.");

        Results = string.IsNullOrWhiteSpace(query)
            ? []
            : [.. Catalog.Where(c => c.Contains(query, StringComparison.OrdinalIgnoreCase))];
    }
}
