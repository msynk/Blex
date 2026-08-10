using System.Text.Json.Nodes;

namespace Blex.Testing;

/// <summary>
/// A self-contained test fixture around a single store: it creates a <see cref="ManagerBlex"/>,
/// registers the store, and records every action so tests can assert on dispatch behavior without
/// any Blazor or DI setup.
/// </summary>
/// <typeparam name="TStore">The store under test.</typeparam>
public sealed class TestHarnessBlex<TStore> : IDisposable
    where TStore : StoreBaseBlex
{
    /// <summary>Creates a harness, registering <paramref name="store"/> with a fresh manager.</summary>
    public TestHarnessBlex(TStore store) : this(store, null)
    {
    }

    /// <summary>
    /// Creates a harness whose manager runs <paramref name="middleware"/>, so a store can be tested
    /// together with the veto filters and observers that wrap it in the real application.
    /// </summary>
    public TestHarnessBlex(TStore store, IEnumerable<IMiddlewareBlex>? middleware)
    {
        ArgumentNullException.ThrowIfNull(store);
        Store = store;
        Manager = new ManagerBlex(middleware);
        Manager.Register(store);
        Log = Manager.RecordActions();
    }

    /// <summary>The store under test.</summary>
    public TStore Store { get; }

    /// <summary>The manager backing the harness.</summary>
    public ManagerBlex Manager { get; }

    /// <summary>The action log recording all dispatches.</summary>
    public ActionLogBlex Log { get; }

    /// <summary>The current global state tree.</summary>
    public JsonObject State => Manager.CaptureGlobalState();

    /// <summary>The current snapshot of the store under test.</summary>
    public JsonObject Snapshot() => Store.SerializeState();

    /// <inheritdoc />
    public void Dispose() => Log.Dispose();
}
