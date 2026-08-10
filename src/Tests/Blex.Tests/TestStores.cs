using System.Collections.Generic;
using System.Threading.Tasks;
using Blex;

namespace Blex.Tests;

[StoreAttributeBlex(Name = "counter")]
public partial class CounterStore
{
    [StateAttributeBlex] private int _count;
    [StateAttributeBlex] private string _label = "idle";

    [ComputedAttributeBlex]
    private int ComputeDoubleCount() => Count * 2;

    private int _computeCalls;
    public int ComputeCalls => _computeCalls;

    [ComputedAttributeBlex]
    private int ComputeTrackedCount()
    {
        _computeCalls++;
        return Count + 1;
    }

    [ActionAttributeBlex]
    private void OnIncrement() => Count++;

    [ActionAttributeBlex]
    private void OnAdd(int amount) => Count += amount;

    [ActionAttributeBlex]
    private void OnReset()
    {
        Count = 0;
        Label = "idle";
    }

    [ActionAttributeBlex(Name = "LoadData")]
    private async Task OnLoadAsync()
    {
        Label = "loading";
        await Task.Delay(1);
        Count = 42;
        Label = "loaded";
    }

    // Returns ValueTask<T>: the generator must await it (via AsTask) rather than fire-and-forget.
    // The explicit name contains spaces, so it is a display label only; the wrapper is "LoadValue".
    // BLEX011 (discarded return value) is expected here and suppressed via NoWarn in the csproj
    // -- exercising that shape is exactly what this fixture is for.
    [ActionAttributeBlex(Name = "Load Value")]
    private async ValueTask<int> OnLoadValue()
    {
        await Task.Delay(1);
        Count = 7;
        return Count;
    }
}

[StoreAttributeBlex]
public partial class TodoStore
{
    [StateAttributeBlex] private List<string> _items = new();

    [ActionAttributeBlex]
    private void OnAddItem(string text) => Items = new List<string>(Items) { text };
}

[StoreAttributeBlex(Name = "settings", Persist = true)]
public partial class SettingsStore
{
    [StateAttributeBlex] private string _theme = "light";
    [StateAttributeBlex] private int _fontSize = 14;

    [ActionAttributeBlex]
    private void OnSetTheme(string theme) => Theme = theme;
}

[StoreAttributeBlex(Name = "data")]
public partial class DataStore
{
    [StateAttributeBlex] private string _value = "";
    public bool ShouldThrow { get; set; }

    [EffectAttributeBlex]
    private async Task OnLoad(string input)
    {
        await Task.Delay(1);
        if (ShouldThrow)
            throw new InvalidOperationException("boom");
        Value = input;
    }
}

[StoreAttributeBlex(Name = "profile")]
public partial class ProfileStore
{
    [StateAttributeBlex] private string? _userName = "anonymous";
    [StateAttributeBlex] private int _age;

    [ActionAttributeBlex]
    private void OnSignOut() => UserName = null;

    [ActionAttributeBlex]
    private void OnSignIn(string name) => UserName = name;
}

/// <summary>Gated effects for deterministic concurrency tests. Enqueue gates before invoking.</summary>
[StoreAttributeBlex(Name = "fx")]
public partial class EffectConcurrencyStore
{
    [StateAttributeBlex] private string _last = "";
    [StateAttributeBlex] private int _completed;

    public Queue<TaskCompletionSource> Gates { get; } = new();

    private Task NextGate() => Gates.Count > 0 ? Gates.Dequeue().Task : Task.CompletedTask;

    [EffectAttributeBlex(Concurrency = EffectConcurrencyBlex.Latest)]
    private async Task OnSearch(string query, CancellationToken ct)
    {
        await NextGate().WaitAsync(ct);
        Last = query;
        Completed++;
    }

    [EffectAttributeBlex(Concurrency = EffectConcurrencyBlex.Drop)]
    private async Task OnSubmit()
    {
        await NextGate();
        Completed++;
    }

    [EffectAttributeBlex(Concurrency = EffectConcurrencyBlex.Queue)]
    private async Task OnWrite(string value)
    {
        await NextGate();
        Last = Last + value;
        Completed++;
    }

    [EffectAttributeBlex]
    private async Task OnFetch()
    {
        await NextGate();
        Completed++;
    }

    // The gate is intentionally NOT linked to the token: a superseded run keeps executing and
    // then fails, which must not clobber the newest run's error state.
    [EffectAttributeBlex(Concurrency = EffectConcurrencyBlex.Latest)]
    private async Task OnFlaky(bool fail, CancellationToken ct)
    {
        await NextGate();
        if (fail)
            throw new InvalidOperationException("stale-boom");
        Last = "flaky-ok";
        Completed++;
    }

    // Throws a *foreign* cancellation (like an HttpClient timeout) while our token is NOT
    // cancelled -- this must surface as an error, not be swallowed as a benign cancel.
    [EffectAttributeBlex]
    private async Task OnTimeout(CancellationToken ct)
    {
        await Task.Yield();
        throw new TaskCanceledException("simulated http timeout");
    }

    // Queue effect whose runs can fail: a failed predecessor's error must not survive a
    // successful successor (the successor clears the error only after the predecessor ends).
    [EffectAttributeBlex(Concurrency = EffectConcurrencyBlex.Queue)]
    private async Task OnQueuedFlaky(bool fail)
    {
        await NextGate();
        if (fail)
            throw new InvalidOperationException("queued-boom");
        Completed++;
    }

    public int DebouncedBodyRuns { get; private set; }

    // Short enough to keep tests quick, long enough that a burst dispatched in one go cannot
    // straddle it.
    [EffectAttributeBlex(DebounceMilliseconds = 40)]
    private async Task OnTypeAhead(string query)
    {
        DebouncedBodyRuns++;
        await NextGate();
        Last = query;
        Completed++;
    }

    // Debounce gates the burst; Latest cancels a request that is already in flight when the next
    // one gets through.
    [EffectAttributeBlex(DebounceMilliseconds = 40, Concurrency = EffectConcurrencyBlex.Latest)]
    private async Task OnLiveSearch(string query, CancellationToken ct)
    {
        DebouncedBodyRuns++;
        await NextGate().WaitAsync(ct);
        Last = query;
        Completed++;
    }

    [EffectAttributeBlex(DebounceMilliseconds = 40)]
    private async Task OnDebouncedBoom()
    {
        DebouncedBodyRuns++;
        await Task.Yield();
        throw new InvalidOperationException("debounced-boom");
    }
}

/// <summary>
/// A store with one effect that parks on a gate, so a second action can be dispatched while the
/// first is still in flight (the default <c>EffectConcurrencyBlex.Parallel</c> shape).
/// </summary>
[StoreAttributeBlex(Name = "gated")]
public partial class GatedStore
{
    [StateAttributeBlex] private int _count;

    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [EffectAttributeBlex]
    private async Task OnSlow()
    {
        await Gate.Task;
        Count++;
    }

    [EffectAttributeBlex]
    private async Task OnQuick()
    {
        await Task.Yield();
        Count += 10;
    }
}

public record Todo(int Id, string Text, bool Done);

[StoreAttributeBlex(Name = "todos")]
public partial class EntityTodoStore
{
    private static readonly EntityAdapterBlex<Todo, int> Adapter = new(t => t.Id);

    [StateAttributeBlex] private EntityStateBlex<Todo, int> _todos = Adapter.GetInitialState();

    [ComputedAttributeBlex] private int ComputeRemaining()
    {
        var n = 0;
        foreach (var t in Todos.All)
            if (!t.Done) n++;
        return n;
    }

    [ActionAttributeBlex] private void OnAdd(Todo todo) => Todos = Adapter.UpsertOne(Todos, todo);

    [ActionAttributeBlex] private void OnToggle(int id) => Todos = Adapter.UpdateOne(Todos, id, t => t with { Done = !t.Done });

    [ActionAttributeBlex] private void OnRemove(int id) => Todos = Adapter.RemoveOne(Todos, id);
}
