using System.Text.Json.Nodes;

namespace Blex;

/// <summary>
/// Coordinates automatic persistence: rehydrates persistent stores on startup and saves their
/// slice through an <see cref="IStorageBlex"/> after every action (optionally debounced).
/// A store opts in via <c>[StoreAttributeBlex(Persist = true)]</c>. Restored state (undo/redo, time-travel)
/// is also written back so a reload never resurrects pre-restore state. Writes are serialized
/// in dispatch order so a stale payload can never overwrite a newer one.
/// Single-threaded dispatch is assumed (Blazor's model).
/// </summary>
public sealed class StatePersistorBlex : IAsyncDisposable, IDisposable
{
    private const string VersionKey = "__blexVersion";
    private const string StateKey = "state";

    private readonly ManagerBlex _manager;
    private readonly IStorageBlex _storage;
    private readonly PersistenceOptionsBlex _options;
    private readonly HashSet<string> _pending = [];

    // Debounced saves can resume off the dispatch thread when no SynchronizationContext exists
    // (plain hosts); this gate keeps the pending set, the debounce source and the write chain
    // consistent in that case.
    private readonly Lock _saveGate = new();
    private Task _writeChain = Task.CompletedTask;
    private Task _externalChain = Task.CompletedTask;
    private CancellationTokenSource? _debounce;
    private DateTimeOffset? _pendingSince;
    private bool _subscribed;
    private bool _suspended;
    private bool _watching;

    /// <summary>Creates a persistor over a manager and storage sink.</summary>
    public StatePersistorBlex(ManagerBlex manager, IStorageBlex storage, PersistenceOptionsBlex? options = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(storage);
        _manager = manager;
        _storage = storage;
        _options = options ?? new PersistenceOptionsBlex();
    }

    private string KeyFor(IStoreBlex store) => _options.KeyPrefix + store.Name;

    /// <summary>
    /// Loads and applies any previously persisted state for every persistent store, then starts
    /// listening for changes. Restoration does not record new actions. A corrupt or
    /// non-migratable payload is reported, discarded and skipped -- it never prevents startup.
    /// Exceptions from the storage provider itself (e.g. JS interop unavailable during
    /// prerendering) propagate so the host can retry later; the method is safe to call again.
    /// </summary>
    /// <remarks>
    /// Only stores already registered with the manager are rehydrated. In hosts without
    /// <c>&lt;ProviderBlex&gt;</c>, resolve every store from DI (which registers it) before
    /// calling this method.
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.SkipHydration)
            await RehydrateAsync(cancellationToken).ConfigureAwait(false);

        if (!_subscribed)
        {
            _manager.ActionDispatched += OnAction;
            _manager.StateRestored += OnStateRestored;
            _subscribed = true;
        }

        await StartWatchingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Begins applying state written by other instances (other tabs), when
    /// <see cref="PersistenceOptionsBlex.SyncAcrossTabs"/> is on and the storage can report it.
    /// </summary>
    private async Task StartWatchingAsync(CancellationToken cancellationToken)
    {
        if (!_options.SyncAcrossTabs || _watching || _storage is not IExternalStorageChangeBlex watchable)
            return;

        _watching = true;
        watchable.ExternalChange += OnExternalChange;
        try
        {
            await watchable.StartWatchingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Setting up the watch failed (interop unavailable during prerender, say). Roll back so
            // a later StartAsync can try again rather than leaving a subscription that never fires.
            watchable.ExternalChange -= OnExternalChange;
            _watching = false;
            throw;
        }
    }

    /// <summary>
    /// Applies state another instance wrote. Fired from the storage's own notification, so it must
    /// not throw and must not schedule a save: the value came <em>from</em> storage, and writing it
    /// straight back would bounce the change between tabs.
    /// </summary>
    private void OnExternalChange(string? key)
    {
        // Chained rather than fired off independently: two changes arriving in quick succession
        // would otherwise race on their reads, and the older payload could land last.
        lock (_saveGate)
        {
            _externalChain = ApplyExternalChangeAsync(_externalChain, key);
        }
    }

    private async Task ApplyExternalChangeAsync(Task previous, string? key)
    {
        await previous.ConfigureAwait(true);
        await ApplyExternalChangeAsync(key).ConfigureAwait(true);
    }

    private async Task ApplyExternalChangeAsync(string? key)
    {
        try
        {
            // A null key means the whole area changed (a wholesale clear), so re-read everything.
            var target = key is null ? null : StoreNameFor(key);
            if (key is not null && target is null)
                return; // some other application's key sharing the same storage area

            foreach (var store in _manager.Stores)
            {
                if (!store.Persist || store is not StoreBaseBlex sb)
                    continue;
                if (target is not null && store.Name != target)
                    continue;

                var raw = await _storage.GetAsync(KeyFor(store)).ConfigureAwait(true);

                JsonObject? slice = null;
                if (!string.IsNullOrEmpty(raw))
                {
                    try
                    {
                        slice = Unwrap(store.Name, raw);
                    }
                    catch (Exception ex)
                    {
                        _manager.ReportError("persistence", ex, $"external change for '{store.Name}'");
                        continue;
                    }
                }

                if (slice is null)
                    continue;

                // Suspended: applying is a restore, not an action, and echoing it back to storage
                // would bounce the same value between tabs forever.
                var wasSuspended = _suspended;
                _suspended = true;
                try
                {
                    sb.ApplyRestoredState(slice);
                }
                catch (Exception ex)
                {
                    _manager.ReportError("persistence", ex, $"applying external change to '{store.Name}'");
                    continue;
                }
                finally
                {
                    _suspended = wasSuspended;
                }

                NotifyExternalChange(store.Name, slice);
            }
        }
        catch (Exception ex)
        {
            // Nothing awaits this task, so an escaping exception would surface as an unobserved one.
            _manager.ReportError("persistence", ex, "external change");
        }
    }

    /// <summary>Maps a storage key back to a store name, or <c>null</c> when it is not ours.</summary>
    private string? StoreNameFor(string key)
    {
        var prefix = _options.KeyPrefix ?? "";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var name = key.Substring(prefix.Length);
        foreach (var store in _manager.Stores)
        {
            if (store.Persist && store.Name == name)
                return name;
        }

        return null;
    }

    private void NotifyExternalChange(string storeName, JsonObject state)
    {
        if (_options.OnExternalChange is not { } handler)
            return;

        try
        {
            handler(storeName, state);
        }
        catch (Exception ex)
        {
            _manager.ReportError("persistence", ex, $"external-change callback for '{storeName}'");
        }
    }

    /// <summary>
    /// Reads and applies persisted state for every persistent store, without changing whether the
    /// persistor is listening for saves. <see cref="StartAsync"/> calls this unless
    /// <see cref="PersistenceOptionsBlex.SkipHydration"/> is set; call it directly to hydrate later
    /// (once the user, tenant or feature flags are known), or to re-read storage after an external
    /// change. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// Saving is suspended for the duration so rehydration does not immediately rewrite storage.
    /// A corrupt or non-migratable payload is reported, discarded and skipped; exceptions from the
    /// storage provider itself propagate so the host can retry later.
    /// </remarks>
    public async Task RehydrateAsync(CancellationToken cancellationToken = default)
    {
        // Suspend saving while we restore so rehydration doesn't immediately rewrite storage.
        var wasSuspended = _suspended;
        _suspended = true;
        try
        {
            foreach (var store in _manager.Stores)
            {
                if (!store.Persist || store is not StoreBaseBlex sb)
                    continue;

                // Storage access errors propagate (the storage may simply not be ready yet);
                // payload errors below are contained per store.
                var raw = await _storage.GetAsync(KeyFor(store), cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(raw))
                {
                    NotifyRehydrated(store.Name, null);
                    continue;
                }

                JsonObject? slice = null;
                try
                {
                    slice = Unwrap(store.Name, raw);
                }
                catch (Exception ex)
                {
                    // Unreadable payload: report, discard, and drop the stored entry.
                    _manager.ReportError("persistence", ex, $"rehydrating '{store.Name}'");
                    await TryRemoveAsync(store, cancellationToken).ConfigureAwait(false);
                }

                if (slice is null)
                {
                    NotifyRehydrated(store.Name, null);
                    continue;
                }

                try
                {
                    sb.ApplyRestoredState(slice);
                }
                catch (Exception ex)
                {
                    // Applying can throw from a StateChanged subscriber even though the payload
                    // itself is fine -- report, but keep the stored data.
                    _manager.ReportError("persistence", ex, $"applying '{store.Name}'");
                }

                NotifyRehydrated(store.Name, slice);
            }
        }
        finally
        {
            _suspended = wasSuspended;
        }
    }

    /// <summary>Reports a store's rehydration outcome, containing a throwing callback.</summary>
    private void NotifyRehydrated(string storeName, JsonObject? state)
    {
        if (_options.OnRehydrated is not { } handler)
            return;

        try
        {
            handler(storeName, state);
        }
        catch (Exception ex)
        {
            // A user callback must not abort hydration for the stores behind it.
            _manager.ReportError("persistence", ex, $"rehydrated callback for '{storeName}'");
        }
    }

    /// <summary>
    /// Parses a stored payload, unwrapping the version envelope and running migrations.
    /// Returns <c>null</c> when the payload should be discarded.
    /// </summary>
    private JsonObject? Unwrap(string storeName, string raw)
    {
        if (JsonNode.Parse(raw) is not JsonObject parsed)
            return null;

        var storedVersion = 0;
        var state = parsed;

        // An envelope has exactly { "__blexVersion": n, "state": {...} }; requiring the exact
        // shape prevents misreading a raw store payload that happens to contain a key named
        // "__blexVersion".
        if (parsed.Count == 2
            && parsed.TryGetPropertyValue(VersionKey, out var versionNode)
            && parsed.TryGetPropertyValue(StateKey, out var stateNode))
        {
            storedVersion = versionNode?.GetValue<int>() ?? 0;
            state = stateNode as JsonObject ?? new JsonObject();
            state = (JsonObject)state.DeepClone(); // detach from the envelope parent
        }

        if (storedVersion == _options.Version)
            return state;

        return _options.Migrate?.Invoke(storeName, storedVersion, state);
    }

    private string Wrap(JsonObject state)
    {
        if (_options.Version <= 0)
            return state.ToJsonString();

        var envelope = new JsonObject
        {
            [VersionKey] = _options.Version,
            [StateKey] = state,
        };
        return envelope.ToJsonString();
    }

    private async ValueTask TryRemoveAsync(IStoreBlex store, CancellationToken cancellationToken)
    {
        try
        {
            await _storage.RemoveAsync(KeyFor(store), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Removal of a corrupt entry is best-effort.
        }
    }

    private void OnAction(ActionContextBlex context)
    {
        if (_suspended || !context.Store.Persist)
            return;

        try
        {
            Schedule(context.Store.Name);
        }
        catch (Exception ex)
        {
            // This handler sits directly on ActionDispatched; an escaping exception would
            // break the dispatch pipeline for every other observer.
            _manager.ReportError("persistence", ex, $"scheduling '{context.Store.Name}'");
        }
    }

    private void OnStateRestored()
    {
        if (_suspended)
            return;

        try
        {
            // A restore (undo/redo, time-travel) changed every store at once; write back all
            // persistent slices so a reload matches what the user sees.
            foreach (var store in _manager.Stores)
            {
                if (store.Persist)
                    Schedule(store.Name);
            }
        }
        catch (Exception ex)
        {
            _manager.ReportError("persistence", ex, "scheduling restored state");
        }
    }

    private void Schedule(string storeName)
    {
        lock (_saveGate)
        {
            _pending.Add(storeName);

            if (_options.DebounceInterval is not { } interval)
            {
                SavePendingLocked();
                return;
            }

            // Trailing-edge debounce would postpone forever under a steady action stream;
            // enforce the optional max-latency cap.
            var now = DateTimeOffset.UtcNow;
            _pendingSince ??= now;
            if (_options.DebounceMaxDelay is { } maxDelay && now - _pendingSince >= maxDelay)
            {
                CancelDebounceLocked();
                SavePendingLocked();
                return;
            }

            CancelDebounceLocked();
            _debounce = new CancellationTokenSource();
            _ = DebouncedSaveAsync(interval, _debounce.Token);
        }
    }

    private void CancelDebounceLocked()
    {
        if (_debounce is { } previous)
        {
            previous.Cancel();
            previous.Dispose();
            _debounce = null;
        }
    }

    private async Task DebouncedSaveAsync(TimeSpan interval, CancellationToken token)
    {
        try
        {
            // No ConfigureAwait(false): stay on the captured context (the Blazor dispatcher)
            // when one exists, so store serialization runs on the dispatch thread.
            await Task.Delay(interval, token);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a newer action or disposed
        }

        lock (_saveGate)
        {
            SavePendingLocked();
        }
    }

    /// <summary>Serializes each pending store now and chains the writes in order. Caller holds <see cref="_saveGate"/>.</summary>
    private void SavePendingLocked()
    {
        _pendingSince = null;
        if (_pending.Count == 0)
            return;

        // Snapshot first: SerializeState is user code and may dispatch, which re-enters
        // Schedule (the lock is reentrant) and would otherwise mutate the set mid-enumeration.
        var pending = new string[_pending.Count];
        _pending.CopyTo(pending);
        _pending.Clear();

        foreach (var name in pending)
        {
            var store = _manager.GetStore(name);
            if (store is null)
                continue;

            string json;
            try
            {
                var state = store.SerializeState();
                if (_options.Partialize is { } partialize)
                {
                    // A null projection means "do not persist this store right now"; leave whatever
                    // is already in storage alone rather than writing an empty payload over it.
                    var projected = partialize(name, state);
                    if (projected is null)
                        continue;

                    // Returning a nested node of the snapshot (state["Inner"]) is a natural thing
                    // to write, but a JsonNode cannot be parented twice, so Wrap's envelope would
                    // reject it. Detach first rather than making the caller remember.
                    state = projected.Parent is null ? projected : (JsonObject)projected.DeepClone();
                }

                json = Wrap(state);
            }
            catch (Exception ex)
            {
                _manager.ReportError("persistence", ex, $"serializing '{name}'");
                continue;
            }

            var key = KeyFor(store);
            _writeChain = WriteAsync(_writeChain, key, json);
        }
    }

    private async Task WriteAsync(Task previous, string key, string json)
    {
        await previous.ConfigureAwait(false);
        try
        {
            await _storage.SetAsync(key, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _manager.ReportError("persistence", ex, $"writing '{key}'");
        }
    }

    /// <summary>
    /// Forces any debounced pending state to be written now and waits for all queued writes to
    /// complete. Useful before navigating away or in tests.
    /// </summary>
    public Task FlushAsync()
    {
        Task writes;
        Task external;
        lock (_saveGate)
        {
            CancelDebounceLocked();
            SavePendingLocked();
            writes = _writeChain;

            // Incoming cross-tab changes are part of "settle everything in flight" too: without
            // this, a test (or a page about to navigate away) could observe a change that has been
            // reported but not yet applied.
            external = _externalChain;
        }

        return writes.IsCompletedSuccessfully && external.IsCompletedSuccessfully
            ? Task.CompletedTask
            : Task.WhenAll(writes, external);
    }

    /// <summary>Removes every persistent store's saved slice from storage.</summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Task chain;
        lock (_saveGate)
        {
            CancelDebounceLocked();
            _pending.Clear();
            _pendingSince = null;
            chain = _writeChain;
        }

        await chain.ConfigureAwait(false);
        foreach (var store in _manager.Stores)
        {
            if (store.Persist)
                await _storage.RemoveAsync(KeyFor(store), cancellationToken).ConfigureAwait(false);
        }
    }

    private void Unsubscribe()
    {
        if (_watching && _storage is IExternalStorageChangeBlex watchable)
        {
            watchable.ExternalChange -= OnExternalChange;
            _watching = false;
        }

        if (!_subscribed)
            return;

        _manager.ActionDispatched -= OnAction;
        _manager.StateRestored -= OnStateRestored;
        _subscribed = false;
    }

    /// <summary>Stops listening, flushes any pending writes and waits for them to complete.</summary>
    public async ValueTask DisposeAsync()
    {
        Unsubscribe();
        await FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronous disposal, for DI containers that are torn down with <see cref="IDisposable"/>.
    /// </summary>
    /// <remarks>
    /// A container registered as a singleton is disposed through whichever interface the host
    /// calls, and <c>ServiceProvider.Dispose()</c> throws outright on a service that only offers
    /// <see cref="IAsyncDisposable"/> -- so both have to exist. This one stops listening and hands
    /// any debounced state to the storage, but does not wait for the writes to finish: blocking a
    /// UI thread on storage during shutdown risks a deadlock. A synchronous
    /// <see cref="IStorageBlex"/> (MAUI <c>Preferences</c>, a file) completes inline either way;
    /// prefer <see cref="DisposeAsync"/> when the storage is genuinely asynchronous.
    /// </remarks>
    public void Dispose()
    {
        Unsubscribe();
        lock (_saveGate)
        {
            CancelDebounceLocked();
            SavePendingLocked();
        }
    }
}
