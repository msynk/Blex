using System.Text.Json.Nodes;

namespace Blex;

/// <summary>Configuration for <see cref="StatePersistorBlex"/>.</summary>
public sealed class PersistenceOptionsBlex
{
    /// <summary>Prefix prepended to every store's storage key. Defaults to <c>"blex:"</c>.</summary>
    public string KeyPrefix { get; set; } = "blex:";

    /// <summary>
    /// When set, writes are coalesced: a burst of actions produces a single storage write after
    /// this quiet period instead of one write per action. Pending writes are flushed on dispose
    /// (and can be forced with <see cref="StatePersistorBlex.FlushAsync"/>). Defaults to <c>null</c>
    /// (write immediately after every action).
    /// </summary>
    public TimeSpan? DebounceInterval { get; set; }

    /// <summary>
    /// Upper bound on how long a debounced write may be postponed. Debouncing is trailing-edge,
    /// so a steady stream of actions would otherwise defer saving indefinitely; when the oldest
    /// pending change exceeds this age, a save is forced. Defaults to <c>null</c> (no cap).
    /// Only meaningful together with <see cref="DebounceInterval"/>.
    /// </summary>
    public TimeSpan? DebounceMaxDelay { get; set; }

    /// <summary>
    /// Schema version of the persisted payload. When greater than zero, saved state is wrapped in
    /// a version envelope; on load, a stored version that differs from this value is passed to
    /// <see cref="Migrate"/> (or discarded when no migration is registered). Bump this whenever a
    /// persisted store's shape changes. Defaults to <c>0</c> (no envelope, no version checking).
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Migration hook invoked when a persisted payload's version differs from <see cref="Version"/>.
    /// Receives the store name, the stored version (<c>0</c> for pre-versioning payloads) and the
    /// stored state; returns the migrated state, or <c>null</c> to discard the payload.
    /// </summary>
    public Func<string, int, JsonObject, JsonObject?>? Migrate { get; set; }

    /// <summary>
    /// Selects which part of a store's snapshot is written to storage (zustand-persist's
    /// <c>partialize</c>). Receives the store name and its full snapshot; return the subset to
    /// save, or <c>null</c> to skip persisting that store entirely for this write.
    /// </summary>
    /// <remarks>
    /// Keys the returned object omits are simply absent from the payload, and rehydration leaves
    /// the corresponding fields at their declared defaults -- which is exactly what you want for
    /// caches, transient UI flags and anything too large or too sensitive to store. Mutating the
    /// supplied snapshot in place is allowed: it is freshly serialized for this write and is not
    /// shared with the store.
    /// </remarks>
    /// <example><code>
    /// options.Partialize = (store, state) => { state.Remove("SearchResults"); return state; };
    /// </code></example>
    public Func<string, JsonObject, JsonObject?>? Partialize { get; set; }

    /// <summary>
    /// Invoked once per persistent store after rehydration, with the state that was applied, or
    /// <c>null</c> when there was nothing stored (or the payload was discarded). The counterpart to
    /// zustand's <c>onRehydrateStorage</c>; handy for logging or for kicking off a refresh once the
    /// cached state is in place. Failures are reported through <see cref="ManagerBlex.OnError"/>.
    /// </summary>
    public Action<string, JsonObject?>? OnRehydrated { get; set; }

    /// <summary>
    /// When <c>true</c>, <see cref="StatePersistorBlex.StartAsync"/> begins saving but does not
    /// restore anything, leaving rehydration to an explicit
    /// <see cref="StatePersistorBlex.RehydrateAsync"/> call (zustand's <c>skipHydration</c>).
    /// Use it when stored state must not be applied until the app knows who is logged in, which
    /// tenant is active, or that a migration has run. Defaults to <c>false</c>.
    /// </summary>
    public bool SkipHydration { get; set; }

    /// <summary>
    /// Applies state written by another instance of the app -- another browser tab sharing the same
    /// <c>localStorage</c> -- as soon as it lands, instead of only at the next startup. Requires a
    /// storage that implements <see cref="IExternalStorageChangeBlex"/>; ignored otherwise.
    /// Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off, two open tabs quietly fight: each saves after every action, so whichever acted last
    /// wins and the other's changes disappear on its next reload. On, a change made in one tab is
    /// re-read and applied in the others, so they converge. Last writer still wins for a genuinely
    /// concurrent edit -- this makes the outcome visible and consistent rather than silent.
    /// </para>
    /// <para>
    /// Incoming state is applied as a restore, not an action: it does not record a time-travel
    /// entry and is not written straight back. It does raise the usual change notification, so the
    /// UI updates. <see cref="Migrate"/> runs on it exactly as it does at startup, so a tab running
    /// an older build still reads what a newer one wrote.
    /// </para>
    /// </remarks>
    public bool SyncAcrossTabs { get; set; }

    /// <summary>
    /// Called when state written by another instance has been applied to a store, with the store
    /// name and the applied slice. Use it to notify the user ("updated in another tab") or to
    /// re-run work that depended on the previous value. Only fires when
    /// <see cref="SyncAcrossTabs"/> is on.
    /// </summary>
    public Action<string, JsonObject>? OnExternalChange { get; set; }
}
