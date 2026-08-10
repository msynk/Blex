using System.Text.Json.Nodes;

namespace Blex.Testing;

/// <summary>
/// A self-contained test fixture around a single store: it creates a <see cref="BlexManager"/>,
/// registers the store, and records every action so tests can assert on dispatch behavior without
/// any Blazor or DI setup.
/// </summary>
/// <typeparam name="TStore">The store under test.</typeparam>
public sealed class BlexTestHarness<TStore> : IDisposable
    where TStore : BlexStoreBase
{
    /// <summary>Creates a harness, registering <paramref name="store"/> with a fresh manager.</summary>
    public BlexTestHarness(TStore store) : this(store, null)
    {
    }

    /// <summary>
    /// Creates a harness whose manager runs <paramref name="middleware"/>, so a store can be tested
    /// together with the veto filters and observers that wrap it in the real application.
    /// </summary>
    public BlexTestHarness(TStore store, IEnumerable<IBlexMiddleware>? middleware)
    {
        ArgumentNullException.ThrowIfNull(store);
        Store = store;
        Manager = new BlexManager(middleware);
        Manager.Register(store);
        Log = Manager.RecordActions();
    }

    /// <summary>The store under test.</summary>
    public TStore Store { get; }

    /// <summary>The manager backing the harness.</summary>
    public BlexManager Manager { get; }

    /// <summary>The action log recording all dispatches.</summary>
    public BlexActionLog Log { get; }

    /// <summary>The current global state tree.</summary>
    public JsonObject State => Manager.CaptureGlobalState();

    /// <summary>The current snapshot of the store under test.</summary>
    public JsonObject Snapshot() => Store.SerializeState();

    /// <inheritdoc />
    public void Dispose() => Log.Dispose();
}
