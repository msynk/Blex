# Changelog

All notable changes to Blex are documented here.

## [Unreleased]

### Added

- **`OnActionError`: a hook for the action bodies themselves failing.** Middleware could see an
  action before and after it ran, but never that it *threw* - and an effect deliberately swallows
  its exception into `XxxError` so the UI can render it, which meant nothing else ever heard about
  the failure. `IBlexMiddleware.OnActionError(BlexActionErrorContext)`, `BlexManager.ActionFailed`
  and `options.OnActionError` report it, with the store, action, arguments and exception. The hook
  observes and cannot handle: the exception continues on its way exactly as before, so adding a
  crash reporter never changes what the application does. Cancellation is not reported - an
  `OperationCanceledException` is the documented outcome of `CancelXxx()`, of a `Latest` run being
  superseded and of disposing a store, and burying real failures under a type-ahead's worth of those
  would defeat the point.
- **Multi-step history navigation.** `BlexHistory.Jump(n)`, `JumpToPast(index)` and
  `JumpToFuture(index)` (redux-undo's `jump`/`jumpToPast`/`jumpToFuture`) move several steps in one
  restore - the stacks are walked first and only the destination is applied, so a ten-step undo
  costs one render, one storage write and one `Changed` raise instead of ten of each. `UndoLabels`
  and `RedoLabels` name the steps in navigation order, which is what a history panel needs and what
  `NextUndoLabel` alone could not provide.
- **`groupBy` for history.** Where a filter drops an action from the stack entirely, grouping keeps
  it and folds it into the step already open, so a burst of related actions - every keystroke in one
  field, every frame of one drag - costs a single undo. Only consecutive actions merge: a
  differently-keyed action, an ungrouped one, a `Checkpoint`, an undo or a redo all close the open
  group, so a step the user has already stepped away from cannot quietly absorb new work.
- **Hydration status on the persistor.** `HasHydrated`, the `Hydrated` event and
  `WhenHydratedAsync()` (zustand's `hasHydrated`/`onFinishHydration`) say whether a persisted store
  holds the user's data or its declared defaults. Hydration is not instantaneous - browser storage
  sits behind JS interop, a Blazor Server prerender cannot reach it, `SkipHydration` defers it on
  purpose - so without this a full cart briefly renders as "0 items". The awaitable form is released
  on disposal, so a persistor torn down before it ever hydrated leaves nobody awaiting forever.
- **`options.Merge`**, for deciding how a stored payload meets the live store when the default
  shallow merge is not enough: a nested object combined key by key rather than replaced wholesale,
  or a value the live store must win on because it was assigned before hydration finished. Runs for
  cross-tab changes too, so both paths agree on what "applying stored state" means; returning `null`
  (or throwing, which is reported) discards the payload rather than applying half of it.
- **`BlexComparers.Sequence<T>()` / `Set<T>()`.** A selector projecting a collection defeats itself:
  `() => store.Items.Where(...)` builds a new object on every evaluation, so the default reference
  comparison always reports a change and the subscription fires on every action - exactly what
  selecting a projection was meant to avoid. These are the content comparers that fix it, the job
  zustand's `shallow` does.
- **Stores are `IDisposable`, and disposal cancels in-flight effects.** A DI container disposes the
  stores it created - a Blazor Server circuit ending, a test scope closing - and until now an effect
  started moments before teardown carried on resuming against a dead renderer, with any pending
  debounce timer still running. The generator now emits a `CancelEffects` override that cancels
  every effect token source and debounce gate. It counts as the effect's own cancellation, so it
  populates no `Error`, and the store stays usable afterwards: disposal ordering must not be able to
  strand a final persistence flush. `Dispose`, `IsDisposed` and `CancelEffects` join the BLEX005
  reserved names.
- **`BlexEntityState.TryGet(id, out entity)`**, the unambiguous lookup `Find` cannot be: for a struct
  entity `default` is a legitimate value, so `Find` cannot distinguish "absent" from "present and
  zero", and `Contains` followed by `Find` hashes the key twice. Plus
  `BlexEntityAdapter.SelectId(entity)` and `SortComparer`, so callers can key entities the way the
  adapter does instead of repeating the key expression at every call site, where a second copy can
  silently diverge.
- **The DevTools monitor's recording controls.** `PAUSE_RECORDING` stops streaming actions without
  freezing the app (they still apply, and still reach middleware and subscribers); `LOCK_CHANGES`
  vetoes every action while leaving time-travel restores working, which is the point of the button.
  Both are readable as `manager.IsDevToolsPaused` / `manager.AreChangesLocked`, and both are cleared
  when the bridge disconnects - a monitor closed while locked would otherwise leave the application
  permanently frozen with no remaining way to unfreeze it.

- **`manager.CreateSelector(...)`: a memoized cross-store derived value** -- what
  `[BlexComputed]` is to a single store, lifted to the manager. `Subscribe(selector, …)`
  answers a different question (tell one observer when a projection moved) and evaluates eagerly
  after every action whether anyone reads it or not; this one is lazy and shared, so three
  components binding to the same cross-store total pay for one computation per action instead of
  three, and an action none of them read costs a flag write. Selectors compose, and a failed
  computation is not cached.
- **`BlexEntityState`'s indexer**, `state[id]`, for the lookups where absence is a bug rather than a
  case to handle.
- **`manager.WaitForAsync(...)`** in `Blex.Testing`, for a condition that spans stores or that the
  store being driven will not report. Re-checked after every action *and* every restore, where
  `store.WaitForAsync` watches one store's notifications.
- **Test-harness support for more than one store.** `harness.Register(store)` brings a second store
  onto the harness's manager, so cross-store coordination is testable without abandoning the harness
  and hand-wiring a manager - the setup it exists to remove. `harness.Failures` records every action
  whose body threw, which an effect otherwise hides inside `XxxError`.
- **Trimming/AOT guidance.** Snapshots go through `System.Text.Json`, whose reflection-based
  metadata a trimmed publish (the Blazor WebAssembly release default) can strip. Pointing
  `BlexJson.Configure(o => o.TypeInfoResolver = AppJsonContext.Default)` at a source-generated
  context now has a documented, tested path through every generated `SerializeState`.

### Changed

- **Middleware runs in registration order.** DI-resolved middleware used to be hoisted ahead of
  instance and delegate middleware regardless of how they were interleaved, so a pipeline read in
  one order and ran in another - and since the first veto short-circuits the rest, that could decide
  which filter got consulted at all. Types and instances now share one ordered list. Middleware
  registered straight into the service collection, rather than through the options, is appended
  after it (and de-duplicated). A middleware type is also registered concretely, so injecting it as
  `IBlexMiddleware` or as itself yields the same instance within a scope rather than two copies with
  divergent state.

### Fixed

- **The DevTools "Pause recording" and "Lock changes" buttons could never reach Blex.** The JS
  bridge advertised `features: { pause: false }` and did not list `lock` at all, and the extension
  allows exactly the features listed as true - so the monitor hid both controls. They are now
  advertised and handled.
- **A second browser-storage instance silently stole the first one's cross-tab listener.** The JS
  module is cached per URL and held its `storage` watcher in a single slot, so a second instance
  (a second Blazor Server circuit, or a scope re-created by a render-mode switch) detached the
  previous listener on registration, and that circuit stopped receiving cross-tab changes with no
  error anywhere. Watchers are keyed by handle now, the way DevTools connections already were.
- **One failure was reported once per dispatch frame.** An action calling another action - or an
  effect body calling one - sends the same exception out through several frames, so a crash reporter
  saw one nested failure as several. The innermost frame, which names the action that actually
  threw, reports it and the outer ones re-throw silently.
- The `Blex.Blazor` bridges had no test coverage at all -- the test project did not even reference
  the assembly, yet the connector and the storage bridge hold the real logic (connection handles,
  watcher handles, which module function gets which arguments). Both are now covered through
  hand-written interop fakes, which is how the two handle bugs above were found.
- **`BlexHistory.UndoLabels`/`RedoLabels` rebuilt their array on every access**, which made the
  natural way to render a history panel (`for (i < UndoLabels.Count) … UndoLabels[i]`) quadratic.
  They are cached and invalidated whenever the stacks change.
- **A synchronous action dispatched while an async action of the same store was awaiting bypassed
  every veto filter.** The previous release fixed this for asynchronous actions but left `Dispatch`
  gating its `BeforeAction` call on the record depth, which is also raised by an unrelated in-flight
  effect - so for as long as any effect was awaiting, `UseFilter(...)` guard rails did not apply to
  plain synchronous actions. Both paths now gate on the synchronous nesting depth, which
  distinguishes "lexically nested inside a running action" (must not re-veto) from "another action
  of this store is still awaiting" (must). Raw property assignments are deliberately unchanged: a
  `Set X` carries no way to tell the action's own body from an outside mutation mid-await, so it
  stays attributed to the action in flight, as documented.
- **A manager-level selector subscription never re-evaluated after a state restore.** It watched
  `ActionDispatched` only, but undo/redo, DevTools time-travel and incoming cross-tab changes all
  move state without dispatching anything - so the subscription kept reporting a value the
  application no longer held. It now observes `StateRestored` as well, with the same error isolation.
- **A `Checkpoint` on unchanged state left the open history group open.** The no-op short-circuit
  returned before clearing the group key, so the next action merged into a step the user had already
  asked to close.

- **A vetoed effect no longer leaves a trace.** `BlexPreActionContext.Cancel()` promises the action
  does not run, but the generated effect wrapper took the decision *after* it had already
  incremented the loading counter, cleared `XxxError`, cancelled the predecessor of a `Latest`
  effect and taken a `Queue` slot. The veto is now evaluated before any of that, so a vetoed
  invocation flips nothing, supersedes nothing and delays nothing behind it. `Drop`ped invocations
  still never reach the pipeline at all - nothing was dispatched. New `StoreBase.BeginAction` /
  `DispatchApprovedAsync` split the decision from the dispatch (both are reserved names for
  BLEX005).
- **`StatePersistor` implemented only `IAsyncDisposable`**, so any host that disposed its container
  synchronously - what `ServiceProvider.Dispose()` does, and what a singleton registration invites -
  threw `InvalidOperationException` at shutdown. It now implements `IDisposable` too, unsubscribing
  and handing pending state to storage without blocking a UI thread on the write.
- **The prerender-to-interactive handoff had no error isolation.** It was the one restore path where
  a failure escaped: a slice that would not apply threw straight out of `<BlexProvider>`'s
  initialization and took down the whole application at startup - over a render optimization whose
  worst case is simply rendering twice. Failures are now contained per store and reported through
  `OnError` with the new source `"prerender"`, matching every other restore path. The same applies
  to the persist callback, where one store's failure used to strand every store behind it.
- **DevTools bridge failures bypassed `OnError`.** A missing static asset, a failed import or an
  interop send that faulted went straight to `Console.Error`, even though the manager - and
  therefore the application's error sink - was right there. They now report through `OnError` with
  source `"devtools"`, and fall back to stderr only when no manager is attached.
- **A failed browser-storage write was silently swallowed.** The JS bridge caught quota exhaustion,
  private mode and disabled storage, logged a console warning and returned as if the write had
  succeeded - so the app went on believing state was saved that never was. The failure now reaches
  `OnError` with source `"persistence"` like every other non-fatal pipeline error.
- **`BlexEntityState`'s public constructor aliased the collections it was given.** A caller who kept
  a reference to the `Dictionary<,>` or list they passed could mutate an "immutable" state in place,
  and reference-based change detection would never notice. Both halves are now copied. The adapter's
  own internal path is unaffected.
- **A selector subscription leaked its handler when `fireImmediately` threw.** The store's
  `StateChanged` handler was attached before the immediate callback ran, so a callback that threw
  left the subscription attached with the caller holding no token to dispose it - a handler that
  kept firing for the lifetime of the store and could not be stopped. Both the store-level and the
  new manager-level selector detach before letting the exception surface.
- **`Blex.Maui` registrations were rejected by DI scope validation.** MAUI has no scopes, so the
  startup initializer resolves everything from the root provider - illegal for the scoped default
  the core helpers use. `UseBlex()` and the new `MauiAppBuilder` helpers register singletons, and
  the initializer now reports an actionable error through `OnError` instead of failing
  `MauiApp.Build()` when it cannot resolve its services.
- **Reopening the Redux DevTools monitor showed a blank state.** The extension's `START` message
  was ignored, so nothing appeared until the next action; the current tree is now re-sent as the
  initial snapshot.
- `BlexComponentBase` no longer discards the render task, which turned a teardown race into an
  unobserved task exception; shutdown cases are ignored and anything else goes to the error
  boundary via `DispatchExceptionAsync`. `<BlexProvider>` likewise tolerates `TaskCanceledException`
  and `ObjectDisposedException` from interop torn down mid-start.

- **Veto filters were silently bypassed by overlapping async actions.** An action dispatched while
  another asynchronous action of the same store was still awaiting skipped the `BeforeAction`
  pipeline entirely, so `UseFilter(...)` guard rails did not apply to it. Since `Parallel` is the
  default effect concurrency, this was the common case for overlapping effects. The veto is now
  gated on synchronous nesting depth, so lexically nested mutations still inherit their enclosing
  action's decision while genuinely concurrent invocations each get their own.
- **Error isolation now covers the whole observer surface.** `StateChanged`/`PropertyChanged`
  subscribers, raw `ActionDispatched`/`StateRestored` handlers, `BlexHistory.Changed` handlers and
  subscription *filters* were all invoked unprotected, so a single throwing handler aborted every
  handler behind it - and with it persistence, undo/redo and DevTools recording. Every one of these
  is now invoked per handler with exceptions contained and reported through `OnError`.
- **`RedactDevToolsKeys` matched case-sensitively** while state slices are keyed by the generated
  PascalCase property name (`Token`) and action payloads by the camelCase parameter name (`token`),
  so a redaction failed open and leaked exactly what it was meant to hide - including for the
  spelling used in the README's own example. Matching is now case-insensitive.
- **`HandleDevToolsMessage` could throw back into JS interop.** The message comes from a browser
  extension and was read with `GetValue<string>()`, which throws when a field is a different JSON
  kind. Malformed messages are now contained and reported instead of tearing down the circuit.
- **Duplicate store names are reported.** Two stores sharing a name silently shadowed each other in
  the global state tree, in DevTools and - most damagingly - under the same persistence storage key.
  `Register` now reports the collision through `OnError`.
- **Entity adapter no-ops allocated a new state.** `AddMany`/`UpsertMany`/`RemoveMany`/`RemoveAll`/
  `SetAll` with nothing to do, and `UpdateOne`/`UpdateMany`/`Map` whose updater returned an equal
  entity, returned a new instance. State compares by reference, so each of those raised a change
  notification and recorded a phantom time-travel action. They now return the same instance.
- **`EntityState` no longer deserializes into an instance that throws on first use** when a
  persisted payload is missing or nulls out its `ids`/`entities` half.
- **The DevTools JS bridge kept its connection in a module-level slot.** ES modules are cached per
  URL, so a provider that was torn down and re-created had the outgoing instance's `disconnect()`
  tear down the incoming instance's live connection. Connections are now keyed by a handle.
- **The store registry is copy-on-write**, so registering or unregistering a store from inside a
  notification (a lazily-loaded feature arriving mid-dispatch) can no longer invalidate a walk
  already in progress. `BlexManager.Stores` returns an immutable snapshot.
- `BlexComponentBase` no longer attaches subscriptions after disposal, where nothing would detach
  them; `OwnsSubscription` disposes a token handed to it post-disposal rather than leaking it.

### Added

- **`BLEX017`**: a `[Computed]` method returning `void` is now a diagnostic. It previously emitted
  `public void Xxx { get { ... } }` and surfaced as five raw `CS` errors inside generated code.
- **`BLEX006` now covers static stores.** A `static partial class` store emitted instance members
  and a base type it cannot have, producing four raw `CS` errors instead of one Blex diagnostic.
- **`BlexJson.Configure(...)`** registers converters for domain types that appear in `[State]`
  fields. A `JsonSerializerOptions` becomes read-only on first use, so this swaps in a configured
  copy rather than mutating the live instance; `BlexJson.Reset()` restores the defaults.
- **Service lifetimes are configurable.** `AddBlex`, `AddBlexStore`, `AddBlexPersistence` and
  `AddBlexHistory` take a `ServiceLifetime` that still defaults to `Scoped`, so Blazor is
  unchanged. `Blex.Maui` adds `builder.AddBlexStore<T>()`, `builder.AddBlexHistory()` and
  `builder.AddBlexPreferencesPersistence()` on `MauiAppBuilder`, which register singletons.
- **`BlexManager.GetStore(string name)`** - the counterpart to indexing `CaptureGlobalState()`.
- **`BlexEntityAdapter.SetOne` / `SetMany`** replace an entity only when its id is already present -
  unlike `UpsertOne`, they never insert. A save response arriving after the user deleted the row no
  longer resurrects it. Matches Redux Toolkit's `setOne`/`setMany`.
- **Write operations are now no-ops when the value is unchanged.** `SetAll` with an identical
  payload, `UpsertOne`/`UpsertMany` with equal entities and `SetOne`/`SetMany` with an equal
  replacement return the *same* state instance, so polling a list endpoint costs no notification, no
  persistence write and no undo step while the data is unchanged. `SetAll` compares after applying
  the sort comparer, so an unsorted-but-equivalent input is also free.
- **A restored `BlexEntityState` payload is reconciled instead of trusted.** Ids with no matching
  entity are dropped, entities missing from the id list are appended, and duplicate ids are
  collapsed - a hand-edited or partially written payload used to throw `KeyNotFoundException` deep
  inside a render.
- **`BlexHistory` takes an optional filter** deciding which actions earn their own undo step
  (redux-undo's `filter`), plumbed through `AddBlexHistory` on both `IServiceCollection` and
  `MauiAppBuilder`. Without it, typing into a bound property records one undo step per keystroke. A
  filtered action still applies and still advances the present, so undo never lands on a state the
  user never saw; a throwing filter fails open and is reported through `OnError`.
- **`BlexPersistenceOptions.Partialize`** projects each store's snapshot on its way to storage
  (zustand's option of the same name), keeping transient fields out of storage; returning `null`
  skips that store's write and leaves what is stored untouched. Omitted keys rehydrate to their
  declared defaults rather than being wiped.
- **`BlexPersistenceOptions.OnRehydrated`** fires once per persistent store with the slice that was
  applied, or `null` when nothing was stored - enough to tell a returning user from a first visit.
- **`BlexPersistenceOptions.SkipHydration` + `BlexStatePersistor.RehydrateAsync()`** start saving
  without restoring, so per-user state can be applied after sign-in resolves. The restore itself is
  not an action, so it does not immediately write back.
- **`BlexManager.ResetAll()`** resets every registered store to its registered state - Pinia's
  `$reset` across the whole tree. Each store resets as its own normal, vetoable, observable,
  persisted and time-travelable `ResetState` action, and a store that fails is reported and skipped
  rather than aborting the rest.
- **`BLEX018`**: a `[BlexState]`, `[BlexComputed]`, `[BlexAction]` or
  `[BlexEffect]` member outside a `[BlexStore]` class is now a warning. It is the
  one mistake that compiles cleanly and generates nothing at all, which reads like a broken
  generator.

- **`[BlexEffect(DebounceMilliseconds = n)]`** gates an effect behind a quiet period - the
  search-as-you-type case that previously meant hand-rolling a `CancellationTokenSource` and a
  `Task.Delay` in every store. The wait happens before anything observable, so a superseded
  invocation never flips `XxxIsLoading`, never clears `XxxError`, is never offered to middleware and
  records no action: a spinner bound to `IsLoading` no longer blinks once per keystroke, and
  DevTools no longer fills with searches that never happened. Composes with `Concurrency`, which
  still governs the invocations that survive the gate. A negative value is `BLEX019`.
- **`BlexHistory.Checkpoint(label)`** commits the current state as an undo boundary on demand. A
  filter alone leaves filtered work with no boundary of its own, so the next recorded action
  swallows a whole typed edit; a checkpoint marks the points the user would actually step back to.
  Consecutive checkpoints over unchanged state collapse, so a debounced "stopped typing" timer can
  call it freely.
- **`BlexManager.Subscribe<T>(selector, onChanged, comparer, fireImmediately)`** - the cross-store
  counterpart to the store-level selector subscription, in both `Action<T>` and `Action<T, T>`
  shapes. Without it a value derived from two stores had to be recomputed on every action of every
  store.
- **Cross-tab synchronization** (`options.SyncAcrossTabs`). Two open tabs used to quietly fight:
  each saved after every action, so whichever acted last won and the other's changes vanished on
  its next reload. Incoming state is applied as a restore - the UI updates, but it records no
  time-travel entry and is not written straight back (which would bounce the same value between
  tabs) - and is migrated exactly as it is at startup, so a tab on an older build still reads what
  a newer one wrote. `options.OnExternalChange` reports it. Backed by the browser's `storage` event
  in `BlexBrowserStorage`; any storage can opt in via the new `IBlexExternalStorageChange`.
- **`BlexTestHarness.WithFilter<TStore>(predicate)` and `WithMiddleware<TStore>(...)`** create a
  harness whose manager runs the given pipeline. Testing a store together with the veto filter that
  wraps it in production previously meant abandoning the harness and hand-wiring a manager - exactly
  the setup the harness exists to remove.

### Changed

- **`BlexEntityAdapter.UpdateMany` and `Map` are no longer quadratic.** Each delegated to
  `UpdateOne` per id, which copies the whole entity map (and re-sorts it when a comparer is
  configured) every time - so a bulk update over `n` entities did `n` full copies, on precisely the
  operation most likely to run over a large collection. They now apply in one pass over a single
  copy, with identical semantics including the sequential ones.
- **`ResetState()` on a store already holding its initial state is now a no-op.** It used to notify
  every subscriber, write to storage and push an undo entry for a mutation that never happened,
  contradicting the rule every other mutation path follows.
- **`BlexOptions.RedactDevToolsKeys` is additive and composes.** Calling it more than once widens
  the redaction set instead of replacing it, and a `DevToolsStateSanitizer` assigned *before* the
  call still runs. A redaction helper that silently dropped previously registered keys fails open -
  the one thing it must not do. (Assigning `DevToolsStateSanitizer` *after* the call still replaces
  redaction outright.)

### Known limitations

- Time-travel *recording* still coalesces overlapping asynchronous actions on one store into the
  outermost in-flight action, so DevTools shows a single entry for them. Attributing each run
  separately needs per-action dirty tracking plus async-context flow; the middleware/veto pipeline
  is unaffected (every invocation is offered to it independently).

## [0.2.0] - 2026-07-28

### Added

- **XAML data binding**: every store now implements `INotifyPropertyChanged`. `PropertyChanged`
  is raised together with `StateChanged`, always with an empty property name ("all properties
  changed") because memoized `[Computed]` values can change whenever any state field changes -
  so bindings to computed properties stay fresh too. A throwing `StateChanged` subscriber cannot
  starve the raise. `PropertyChanged` joins the reserved member names checked by BLEX005.
- **`Blex.Maui` package**: .NET MAUI integration for native (non-Blazor) apps.
  `builder.UseBlex(...)` registers the manager plus a `IMauiInitializeService` startup
  initializer that mirrors `<BlexProvider>`: it attaches every `AddBlexStore` store to the
  manager, rehydrates persisted state and starts `BlexHistory` recording when `MauiApp.Build()`
  runs. `AddBlexPreferencesPersistence()` persists `[Store(Persist = true)]` stores to OS-native
  MAUI `Preferences` (with the usual debounce/versioning/migration options), and hydration
  failures are contained and reported through `OnError` instead of crashing startup. The package
  targets plain net8.0/net9.0/net10.0 against the neutral MAUI assemblies, so it builds without
  MAUI workloads and resolves from every MAUI platform TFM. Blazor Hybrid apps can also use
  `AddBlexPreferencesPersistence()` to store state outside the WebView profile.

## [0.1.0] - 2026-07-27

First release published to NuGet.

### Packaging

- **Multi-targeting**: `Blex`, `Blex.Blazor` and `Blex.Testing` now build and ship for
  **net8.0, net9.0 and net10.0**, each with framework dependencies matched to its own band. The
  full runtime test suite runs against all three.
- `System.Threading.Lock` (net9+) is aliased to the classic monitor object on net8.0, so lock
  sites compile unchanged on the older framework.
- Packages carry a readme, icon, MIT license expression, repository metadata, tags, symbol
  packages (`.snupkg`) and Source Link, and are produced deterministically. Metadata lives in
  `src/Directory.Build.props`; `dotnet pack src/Blex.slnx -c Release` writes to
  `artifacts/packages`.
- `Blex.Generators` is no longer a separate package. It is bundled into `Blex` under
  `analyzers/dotnet/cs`, and `Blex.Blazor`/`Blex.Testing` depend on `Blex` with
  `PrivateAssets="none"` so the generator reaches consumers that reference only those packages.
- CI packs on every run; a `Release` workflow publishes to nuget.org from a `v*` tag after
  checking that the tag matches `<Version>`.

### Added

- **Action payloads**: generated action/effect wrappers capture their arguments as `ActionArg`s,
  visible on `BlexActionContext.Args` / `BlexPreActionContext.Args` (payload-aware veto
  filters), in `Blex.Testing`'s `ActionLog`, and as the `payload` of the action objects streamed
  to Redux DevTools. Key redaction (`RedactDevToolsKeys`) applies to payloads too.
- **Effect cancellation & concurrency**: an effect whose last parameter is a `CancellationToken`
  gets the token supplied by the generated wrapper plus a `CancelXxx()` method.
  `[Effect(Concurrency = ...)]` supports `Parallel` (default), `Latest` (switchMap), `Drop`
  (exhaustMap) and `Queue` (concatMap). Cancellations never populate `XxxError`, and overlapping
  runs are reference-counted so `XxxIsLoading` stays true until the last run finishes.
- **`store.Batch(name, mutations)`**: group ad-hoc mutations from outside the store into one
  named, vetoable, time-travelable action with a single re-render (Pinia `$patch` equivalent).
- **`store.ResetState()`**: return a store to the state it had when first registered, recorded as
  a normal action. **`store.RestoreState(snapshot)`**: safe public hydration entry point (notifies
  and invalidates computeds, unlike raw `DeserializeState`).
- **Persistence hardening**: corrupt payloads are reported, discarded and removed instead of
  crashing startup; `DebounceInterval` coalesces write bursts; `Version` + `Migrate` provide
  zustand-style versioned migrations; writes are serialized in dispatch order; `FlushAsync()` and
  flush-on-dispose; undo/redo/time-travel restores are written back to storage
  (`BlexManager.StateRestored`).
- **Error hook**: `options.OnError` / `BlexManager.OnError` receives every isolated pipeline
  failure (`BlexError` with source, exception, detail) instead of ad-hoc `Console.Error` writes.
- **Selector subscriptions**: `(previous, current)` overload and `fireImmediately` option.
- **History**: `UndoCount`/`RedoCount` and `NextUndoLabel`/`NextRedoLabel` (per-action labels for
  "Undo Increment"-style UI).
- **Entity adapter**: optional `sortComparer` constructor (keeps `Ids` sorted through every
  operation), `UpdateMany`, `Map`.
- **Testing**: `store.WaitForAsync(condition, timeout)`; recorded actions expose `Args`.
- **Manager**: `Unregister(store)`, `DisconnectDevTools()`, lazily-captured
  `BlexActionContext.GlobalState` (observers that never read the tree no longer pay for
  full-tree serialization).
- **DI**: stores registered via `AddBlexStore<T>()` attach to the manager on first resolution,
  so Blex works without `<BlexProvider>` (console apps, workers, tests).
- **Blazor**: `BlexComponentBase.OwnsSubscription(...)` ties any subscription token to the
  component's lifetime; `Dispose(bool)` is overridable for derived cleanup.
- **Generator diagnostics** BLEX008-BLEX016: static/readonly members, `Latest` without a
  CancellationToken, `async void` actions, discarded action return values, state-field/property
  name conflicts, by-ref parameters, generic methods, record stores, conflicting base types.
  Duplicate detection now also covers user-declared members, `StoreBase` members and
  effect-generated names.
- Sample app: Weather page (cancellable `Latest` effect with loading/error UI), Todos rebuilt on
  `EntityAdapter` + a real `[Effect]`, Counter page demonstrates `Batch`, `ResetState()` and
  labeled undo/redo.
- Repo: GitHub Actions CI, `.editorconfig`, `TreatWarningsAsErrors`, entity adapter benchmarks.

### Fixed (final review round)

- `RedactDevToolsKeys` now recurses into arrays, so secrets inside collection state (entity
  lists, array-typed action args) are redacted as documented.
- `BlexHistory`'s pipeline handlers are exception-isolated: a throwing `Changed` (UI) handler
  can no longer break dispatch for observers registered after history (e.g. lose a persistence
  save) - failures route to `OnError`.
- `EntityAdapter.UpdateOne` re-keying an entity onto an id that already exists merges cleanly
  instead of silently duplicating the id in `Ids`.
- Rehydration no longer deletes valid stored data when a `StateChanged` subscriber throws while
  the restored state is applied (only unreadable payloads are discarded).

### Fixed (verification review round)

- Generator: parameters or state fields named like C# keywords (`@lock`) or generated identifiers
  (`state`, `value`, `__ct`) produced uncompilable or silently wrong code; emitted code now
  `@`-escapes identifiers, `this.`-qualifies field accesses, and prefixes wrapper locals with
  `__blex`.
- Queue effects: a failed predecessor's error no longer survives a successful successor (the
  error is cleared only after the predecessor completes).
- A `StateChanged` subscriber that throws during a standalone set can no longer leak the internal
  dirty flag (which recorded a later no-op batch as a phantom action); the set is still recorded
  even when a subscriber throws, keeping persistence/history in sync with the applied mutation.
- `RestoreGlobalState` freezes in-flight action snapshots first, so a reactor that undoes an
  action (e.g. `history.Undo()` from a subscriber) cannot corrupt later observers' snapshots.
- `StatePersistor` isolates its own handler exceptions from the dispatch pipeline and snapshots
  the pending set before serializing (a store serializer that dispatches re-enters safely).
- README/doc comments no longer overstate cancellation semantics: only cancellation via the
  effect's own token is benign; foreign cancellations are recorded as errors.

### Fixed (adversarial review round)

- Generator: nullable-enum default parameters (`Mode? m = Mode.B`) emitted uncompilable code; a
  C# keyword as an explicit `[Action(Name = ...)]` produced an invalid method name (now treated
  as display label only); control characters in names broke both string literals and XML doc
  comments; a BLEX005 collision emitted the broken source alongside the diagnostic (now the
  diagnostic suppresses emission); collisions with generated backing fields (`__XValid`,
  `__XPending`, ...) are now detected; the transform output is fully equatable so incremental
  caching actually works.
- Effects: a superseded `Latest` run can no longer overwrite the newest run's error state; a
  foreign `OperationCanceledException` (e.g. an HttpClient timeout) is now recorded as an error
  instead of being mistaken for a benign cancel; `Queue` gates use
  `RunContinuationsAsynchronously` and can no longer be bricked by a throwing `StateChanged`
  subscriber (lifecycle setters moved inside the `try`); Queue self-invocation deadlock documented.
- `Unregister` now detaches the store, so its actions stop flowing to middleware/DevTools.
- Lazy `GlobalState` snapshots are frozen before a reactor's follow-up action mutates state, so
  every observer sees the state that belonged to *its* action; `BlexHistory` also refreshes its
  present snapshot after external restores (DevTools jumps), fixing stale undo targets.
- DevTools sanitizers now fail *closed* (state withheld + `OnError` report) instead of leaking
  unsanitized data when a sanitizer throws.
- `StatePersistor`: debounced saves stay on the captured synchronization context and all
  pending/write bookkeeping is lock-guarded (Blazor Server safety); optional `DebounceMaxDelay`
  bounds how long a steady action stream can postpone a save; the version envelope is only
  recognized in its exact shape (no collision with user state containing `__blexVersion`).
- `BlexProvider` no longer starts persistence twice concurrently when Blazor renders during
  initialization; the DevTools connector survives disposal races during fast navigation and
  broadened circuit-teardown exceptions.
- `RestoreGlobalState` applies outside the registry lock, so a `StateChanged` handler that
  registers/unregisters stores can no longer corrupt the restore.

### Fixed

- Restoring a snapshot now sets JSON-`null` properties back to `null`/default (previously a
  reference-typed field could never be restored to `null` by time-travel, undo or persistence).
- A synchronous `[Action]` invoked while an async action of the same store is awaiting no longer
  loses its change notification.
- DevTools receives an action *before* cross-store subscribers run, so reactor-dispatched
  follow-up actions can no longer invert the extension's timeline.
- Malformed DevTools messages and schema-drifted snapshots no longer throw across the JS interop
  boundary; per-store restore failures are isolated and reported.
- Overlapping runs of the same effect no longer corrupt the `IsLoading` flag.
- The generator preserves nullable reference annotations (`string?` state emitted correctly),
  disambiguates hint names by namespace, escapes string literals, fully qualifies JSON types, and
  propagates default parameter values and `params` modifiers to wrappers.
- The DevTools JS bridge unsubscribes its own connection instead of calling the extension-global
  `disconnect()`, and no longer advertises unimplemented features (pause/dispatch).
- `<BlexProvider>` no longer fails app startup under Blazor Server prerendering; browser-storage
  hydration is retried on first interactive render, and history starts recording only after
  hydration (undo baseline is the hydrated state).
- Fire-and-forget persistence writes could land out of order; writes are now chained.
- Sample Counter page no longer leaks a `BlexHistory.Changed` subscription per visit.
