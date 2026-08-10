![Blex](https://raw.githubusercontent.com/msynk/Blex/main/assets/icon.png)

# Blex

[![NuGet](https://img.shields.io/nuget/v/Blex.svg?label=Blex)](https://www.nuget.org/packages/Blex)
[![NuGet](https://img.shields.io/nuget/v/Blex.Blazor.svg?label=Blex.Blazor)](https://www.nuget.org/packages/Blex.Blazor)
[![NuGet](https://img.shields.io/nuget/v/Blex.Maui.svg?label=Blex.Maui)](https://www.nuget.org/packages/Blex.Maui)
[![NuGet](https://img.shields.io/nuget/v/Blex.Testing.svg?label=Blex.Testing)](https://www.nuget.org/packages/Blex.Testing)
[![CI](https://github.com/msynk/Blex/actions/workflows/ci.yml/badge.svg)](https://github.com/msynk/Blex/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/msynk/Blex/blob/main/LICENSE)

Lightweight, source-generator-powered **reactive state management for Blazor and .NET MAUI** - with **Redux DevTools time-travel** built in.

Blex fills a real gap in the Blazor ecosystem. Fluxor is the de-facto Redux library but is widely criticized for boilerplate (separate Action / Reducer / Effect / Feature classes per operation) and for having no first-class DevTools time-travel. Blex keeps the good parts of the Flux model - a single observable state tree, named actions, middleware - while a Roslyn source generator removes the ceremony and a tiny JS bridge wires you straight into the Redux DevTools browser extension.

## Why Blex

| | Fluxor | Blex |
|---|---|---|
| Define a piece of state | Feature + State class | one `[StateAttributeBlex]` field |
| Define an action | Action class + Reducer method | one `[ActionAttributeBlex]` method |
| Action payloads in DevTools/middleware | manual | automatic (`ctx.Args`, DevTools payload) |
| Derived state | manual / selectors | `[ComputedAttributeBlex]` (memoized) |
| Async side-effects | Effect classes | `[EffectAttributeBlex]` (auto loading/error) |
| Effect cancellation / concurrency | manual | `CancellationToken` + `Latest`/`Drop`/`Queue` modes |
| Ad-hoc batched mutations | ✗ | `store.Batch(name, ...)` (Pinia `$patch`-style) |
| Reset to initial state | manual | `store.ResetState()` |
| Middleware | yes | yes (with veto/filter hooks + payload access) |
| Granular re-render | manual selectors | selector `Subscribe(...)` (+ prev/current, fireImmediately) |
| Normalized collections | manual | `EntityAdapterBlex` / `EntityStateBlex` (+ sorting, `UpdateMany`, `Map`) |
| Persistence | 3rd-party | `[StoreAttributeBlex(Persist = true)]` + debounce + versioning/migrations |
| Undo / redo | ✗ | `HistoryBlex` (in-app, labeled entries) |
| Redux DevTools time-travel | ✗ | ✓ built in (+ state/action sanitizers) |
| Error isolation hook | ✗ | `options.OnError` |
| Test helpers | ✗ | `Blex.Testing` harness (+ `WaitForAsync`) |
| Boilerplate | high | minimal (generated) |

## Install

```bash
dotnet add package Blex          # runtime + source generator
dotnet add package Blex.Blazor   # Blazor integration (ProviderBlex, DevTools bridge)
dotnet add package Blex.Maui     # .NET MAUI integration (XAML binding, Preferences persistence)
dotnet add package Blex.Testing  # optional, for unit tests
```

The packages target **net8.0, net9.0 and net10.0**. The Roslyn generator is packed *inside*
`Blex` (under `analyzers/dotnet/cs`), so a package reference is all it takes to light up
codegen - there is no `Blex.Generators` package to install.

## The whole store

```csharp
[StoreAttributeBlex(Name = "counter")]
public partial class CounterStore
{
    [StateAttributeBlex] private int _count;
    [StateAttributeBlex] private int _step = 1;

    [ComputedAttributeBlex] private int  ComputeDoubleCount() => Count * 2;
    [ComputedAttributeBlex] private bool ComputeIsEven()      => Count % 2 == 0;

    [ActionAttributeBlex] private void OnIncrement()       => Count += Step;
    [ActionAttributeBlex] private void OnSetStep(int step) => Step = step;
    [ActionAttributeBlex] private void OnReset()           { Count = 0; Step = 1; }
}
```

The generator emits the reactive `Count`/`Step` properties, the memoized `DoubleCount`/`IsEven`
accessors, the public `Increment()`/`SetStep(int)`/`Reset()` action wrappers, JSON snapshot
support and the `StoreBaseBlex` base type.

### Conventions

- **State**: `[StateAttributeBlex] private T _foo;` → public reactive property `Foo`.
- **Computed**: `[ComputedAttributeBlex]` on a parameterless `ComputeXxx()` / `GetXxx()` method → memoized property `Xxx`,
  automatically invalidated whenever state changes.
- **Actions**: `[ActionAttributeBlex]` on a method named `OnXxx` → public `Xxx(...)` wrapper that batches all the
  mutations inside it into a single, named, time-travel-recorded action. `async Task` methods are
  supported (they update the UI as they go but record as one action). Override the name with
  `[ActionAttributeBlex(Name = "...")]`. Action arguments are captured as the action's payload (visible to
  middleware, subscribers and DevTools).
- Directly assigning a generated property (e.g. `store.Count = 5`) is recorded as a `Set Count` action.
- **Batching from outside**: `store.Batch("Apply preset", () => { store.Count = 10; store.Step = 5; })`
  groups ad-hoc mutations into one named action with a single re-render (the `$patch`/`runInAction`
  equivalent).
- **Reset**: `store.ResetState()` returns the store to the state it had when first registered,
  recorded as a normal, vetoable `ResetState` action.

## Setup

```csharp
// Program.cs
builder.Services.AddBlex(options =>
{
    options.DevToolsName = "My App";
    options.UseMiddleware(ctx => Console.WriteLine($"[blex] {ctx.QualifiedName}"));
});
builder.Services.AddBlexStore<CounterStore>();
builder.Services.AddBlexStore<TodoStore>();
```

```razor
@* App.razor - wrap your router once *@
<ProviderBlex>
    <Router ... />
</ProviderBlex>
```

```razor
@* Counter.razor *@
@inherits ComponentBaseBlex
@inject CounterStore Store

<p>Count: @Store.Count (double: @Store.DoubleCount)</p>
<button @onclick="Store.Increment">+@Store.Step</button>

@code {
    protected override void OnInitialized() => Subscribe(Store);
}
```

`ComponentBaseBlex.Subscribe(...)` re-renders the component whenever a subscribed store changes
and unsubscribes automatically on dispose.

## Granular subscriptions (selectors)

`Subscribe(store)` re-renders on any change to that store. For stores with many independent fields,
subscribe to a projection instead so unrelated changes don't re-render the component:

```csharp
protected override void OnInitialized()
    => Subscribe(Store, () => Store.Count); // re-renders only when Count changes
```

The same primitive is available outside Blazor, with optional previous-value delivery (MobX
`reaction`-style) and `fireImmediately`:

```csharp
using var sub = store.Subscribe(() => store.Count, count => Console.WriteLine(count));
using var log = store.Subscribe(() => store.Count,
    (prev, curr) => Console.WriteLine($"{prev} -> {curr}"), fireImmediately: true);
```

`ManagerBlex` carries the same pair of overloads for a projection that spans stores, so a
cross-store derived value doesn't have to be recomputed on every action of every store:

```csharp
using var sub = manager.Subscribe(
    () => cart.Total > profile.CreditLimit,
    overLimit => _banner = overLimit ? "Over your limit" : null);
```

Keep a manager-level projection cheap - it runs after every action in the application, so read
already-memoized `[ComputedAttributeBlex]` properties rather than recomputing over collections.

## Effects (async with managed loading/error)

`[EffectAttributeBlex]` marks an async method (returning `Task`/`ValueTask`) whose loading and error lifecycle is
generated for you. The body is still recorded as a single, named, time-travelable action.

```csharp
[EffectAttributeBlex]
private async Task OnLoadUser(int id)
{
    var user = await _api.GetUserAsync(id);
    User = user;
}
```

The generator emits `LoadUser(int)` plus reactive `LoadUserIsLoading` (bool) and `LoadUserError`
(`Exception?`) properties. The wrapper keeps `IsLoading` true while any run is in flight (overlapping
runs are reference-counted) and captures any thrown exception into `Error` instead of propagating it.

### Cancellation and concurrency

Give the effect a trailing `CancellationToken` parameter and the generator supplies the token and
emits a `CancelXxx()` method. `Concurrency` selects how overlapping invocations behave, mirroring
the RxJS flattening operators used by NgRx effects:

```csharp
[EffectAttributeBlex(Concurrency = EffectConcurrencyBlex.Latest)]   // switchMap: new call cancels the previous
private async Task OnSearch(string query, CancellationToken ct)
{
    Results = await _api.SearchAsync(query, ct);
}
// generated: Task Search(string query)  +  void CancelSearch()
//            bool SearchIsLoading       +  Exception? SearchError
```

| Mode | Semantics | Typical use |
|---|---|---|
| `Parallel` (default) | all runs proceed concurrently | independent fetches |
| `Latest` | new run cancels the previous (`switchMap`) | type-ahead search |
| `Drop` | ignored while one is running (`exhaustMap`) | double-click-proof submits |
| `Queue` | runs strictly in arrival order (`concatMap`) | ordered writes |

Cancellation through the effect's own token (via `CancelXxx()` or `Latest` supersession) is a
normal outcome and never populates `Error`. A *foreign* `OperationCanceledException` - an
`HttpClient` timeout, or any cancellation when the effect has no token parameter - is a real
failure and is recorded in `Error`.

`Latest` stops a stale response from landing, but it still sends a request per keystroke.
`DebounceMilliseconds` waits for a quiet period first, so only the invocation the user actually
paused on runs at all - the two compose into the complete type-ahead recipe:

```csharp
[EffectAttributeBlex(DebounceMilliseconds = 300, Concurrency = EffectConcurrencyBlex.Latest)]
private async Task OnTypeAhead(string query, CancellationToken ct)
    => Results = await _api.SearchAsync(query, ct);
```

The wait happens before anything observable, which is the point of generating it rather than
hand-rolling it: a superseded invocation never flips `IsLoading`, never clears `Error`, is never
offered to middleware and records no action. A spinner bound to `IsLoading` therefore does not blink
once per keystroke, and DevTools does not fill up with searches that never happened. The gate is per
effect, not per argument.

Every invocation is offered to the middleware/veto pipeline independently, including one started
while another async action of the same store is still awaiting. A vetoed invocation leaves no
trace: it never flips `IsLoading`, never clears `Error`, never supersedes a `Latest` run in flight
and never takes a `Queue` slot. Time-travel *recording*, however, is coalesced: overlapping runs on
one store are folded into the outermost in-flight action, so DevTools shows a single entry for
them. Use `Queue`, `Drop` or `Latest` - or separate stores - when each run needs its own entry.

## Normalized collections (entity adapter)

`EntityAdapterBlex<TEntity, TKey>` generates CRUD operations over an immutable, id-keyed
`EntityStateBlex<TEntity, TKey>` - the same idea as Redux Toolkit's `createEntityAdapter`.

```csharp
[StoreAttributeBlex(Name = "todos")]
public partial class TodoStore
{
    private static readonly EntityAdapterBlex<Todo, int> Adapter = new(t => t.Id);
    [StateAttributeBlex] private EntityStateBlex<Todo, int> _todos = Adapter.GetInitialState();

    [ComputedAttributeBlex] private int ComputeRemaining() => Todos.All.Count(t => !t.Done);

    [ActionAttributeBlex] private void OnUpsert(Todo todo) => Todos = Adapter.UpsertOne(Todos, todo);
    [ActionAttributeBlex] private void OnToggle(int id)    => Todos = Adapter.UpdateOne(Todos, id, t => t with { Done = !t.Done });
    [ActionAttributeBlex] private void OnRemove(int id)    => Todos = Adapter.RemoveOne(Todos, id);
}
```

`EntityStateBlex` exposes `Ids`, `Entities`, `All`, `Count`, `Contains(id)` and `Find(id)`, and
round-trips through JSON for snapshots and persistence. Every operation that changes nothing -
removing an absent id, upserting an empty sequence, an updater that returns an equal entity,
`SetAll` with an unchanged payload - returns the *same* instance, so it raises no notification and
records no action. Polling a list endpoint is therefore free while the data is unchanged. The
adapter also offers `AddMany`, `UpsertMany`, `UpdateMany`, `Map` (transform every entity),
`SetOne`/`SetMany`, `RemoveMany`, `RemoveAll` and `SetAll`, plus an optional sort comparer that
keeps `Ids` ordered after every operation:

```csharp
private static readonly EntityAdapterBlex<Todo, int> Adapter =
    new(t => t.Id, Comparer<Todo>.Create((a, b) => a.DueDate.CompareTo(b.DueDate)));
```

`SetOne`/`SetMany` replace an entity only if its id is already present - unlike `UpsertOne`, they
never insert. That is what you want for a late save response that must not resurrect a row the user
deleted while it was in flight:

```csharp
[EffectAttributeBlex]
private async Task OnSave(Todo todo)
{
    var saved = await _api.PutAsync(todo);
    Todos = Adapter.SetOne(Todos, saved);   // applied only if the id is still there
}
```

A payload restored from storage is reconciled rather than trusted: ids with no matching entity are
dropped, entities missing from the id list are appended, and duplicate ids are collapsed - so a
hand-edited or partially written payload cannot throw mid-render.

## Persistence

Mark a store with `[StoreAttributeBlex(Persist = true)]` and wire up a storage provider; the store is rehydrated
on startup and saved after every action.

```csharp
[StoreAttributeBlex(Name = "settings", Persist = true)]
public partial class SettingsStore { [StateAttributeBlex] private string _theme = "light"; ... }
```

```csharp
// Program.cs (Blazor WebAssembly)
builder.Services.AddBlexLocalStoragePersistence();   // or AddBlexSessionStoragePersistence()
```

`<ProviderBlex>` restores persisted state on init. It also bridges to Blazor's
`PersistentComponentState` automatically (set `PersistComponentState="false"` to opt out), handing
prerendered state to the interactive render to avoid the prerender "double render" flicker. Under
Blazor Server prerendering (where JS interop is unavailable), hydration is automatically retried on
first render instead of crashing startup. For non-Blazor hosts, implement `IStorageBlex` and call
`AddBlexPersistence()`.

Persistence is production-hardened:

- **Corrupt data never breaks startup** - an unreadable payload is reported through `OnError`,
  discarded, and removed from storage.
- **A failed write is reported, not swallowed** - quota exhaustion, private mode or storage being
  disabled reaches `OnError` with source `"persistence"`, so the app can tell the user their
  changes are not being saved instead of pretending they were.
- **Debounce** - `options.DebounceInterval = TimeSpan.FromMilliseconds(300)` coalesces bursts of
  actions into one write (flushed on dispose, or on demand via `persistor.FlushAsync()`).
- **Versioning & migrations** - bump `options.Version` when a persisted store's shape changes and
  supply `options.Migrate` to upgrade (or discard) old payloads, zustand-persist style:

```csharp
builder.Services.AddBlexLocalStoragePersistence(options =>
{
    options.Version = 2;
    options.Migrate = (storeName, fromVersion, state) =>
    {
        if (storeName == "settings" && fromVersion < 2)
            state["Theme"] = "system";   // rename/upgrade old values
        return state;                    // return null to discard instead
    };
});
```

- **Restore write-back** - undo/redo and DevTools time-travel write the restored state back to
  storage, so a reload never resurrects the pre-restore state.
- **Ordered writes** - saves are serialized in dispatch order; a stale payload can't overwrite a
  newer one.
- **Partial persistence** - `options.Partialize` projects each snapshot on its way to storage, so
  transient fields (a search box's text, a cached response) never get written. Keys you omit come
  back as their declared defaults rather than being wiped. Return `null` to skip that store's write
  entirely.
- **Rehydration hooks** - `options.OnRehydrated` fires once per persistent store with the slice that
  was applied, or `null` when nothing was stored - which is how you tell a returning user from a
  first visit.
- **Deferred hydration** - `options.SkipHydration = true` starts saving without restoring; call
  `persistor.RehydrateAsync()` once sign-in resolves, or once the user confirms.
- **Cross-tab sync** - `options.SyncAcrossTabs = true` applies state written by another tab as soon
  as it lands. Without it two open tabs quietly fight: each saves after every action, so whichever
  acted last wins and the other's changes vanish on its next reload. Incoming state is applied as a
  restore (no time-travel entry, and never written straight back, which would bounce it between
  tabs) and is migrated exactly as at startup. `options.OnExternalChange` reports it. Backed by the
  browser's `storage` event for `localStorage`; any storage can opt in by implementing
  `IExternalStorageChangeBlex`.

```csharp
builder.Services.AddBlexLocalStoragePersistence(options =>
{
    options.Partialize = (storeName, state) =>
    {
        if (storeName == "search")
            state.Remove("Query");     // per-visit, never persisted

        return state;                  // return null to skip this store's write
    };

    options.OnRehydrated = (storeName, state) =>
    {
        if (state is null) _telemetry.FirstVisit();
    };
});
```

## Cross-store coordination

React to one store's actions from elsewhere (e.g. trigger an effect on another store):

```csharp
manager.SubscribeTo<CounterStore>(ctx => { /* runs after each CounterStore action */ });
manager.SubscribeToAction("Increment", ctx => { ... });
manager.SubscribeAsync(async ctx => await otherStore.Reload());
```

## Middleware: observe and veto

Middleware sees every action after it applies (including its argument payload via `ctx.Args`), and
can veto an action before it runs - also based on the payload:

```csharp
builder.Services.AddBlex(options =>
{
    options.UseMiddleware(ctx => Console.WriteLine($"{ctx.QualifiedName}({string.Join(", ", ctx.Args)})"));
    options.UseFilter(ctx => !IsReadOnly);   // return false to cancel
    options.OnError = err => _logger.LogWarning(err.Exception, "[blex:{Source}] {Detail}", err.Source, err.Detail);
});
```

`OnError` receives every non-fatal failure Blex isolates from the dispatch pipeline - without it
they go to `Console.Error`. Isolation is per handler and covers the whole observer surface:
`StateChanged`/`PropertyChanged` subscribers (including selector subscriptions and
`ComponentBaseBlex`), `manager.Subscribe(...)` handlers *and* their filters, raw
`ActionDispatched`/`StateRestored` handlers, `HistoryBlex.Changed` and its filter, middleware,
persistence writes, restores, sanitizers, per-store `ResetAll` failures, and the prerender-to-
interactive handoff. One component throwing while it re-renders can never starve the subscribers
behind it, nor the persistence and undo/redo observers that run after them - and a store whose
prerendered slice fails to apply costs a second render, not a failed startup.

Store names key the global state tree, the DevTools slices *and* the persistence storage keys, so
they must be unique. Registering two stores under one name is reported through `OnError` rather
than silently letting them shadow each other - give one an explicit `[StoreAttributeBlex(Name = "...")]`.

## Undo / redo

`HistoryBlex` provides in-app undo/redo over the whole application state, independent of the
DevTools extension:

```csharp
builder.Services.AddBlexHistory();   // <ProviderBlex> starts recording automatically
```

```razor
@inject HistoryBlex History
<button @onclick="History.Undo" disabled="@(!History.CanUndo)">Undo @History.NextUndoLabel</button>
<button @onclick="History.Redo" disabled="@(!History.CanRedo)">Redo @History.NextRedoLabel</button>
```

`NextUndoLabel`/`NextRedoLabel` name the action about to be undone/redone (e.g. "Undo
counter/Increment"); `UndoCount`/`RedoCount` expose stack depths. When persistence is enabled,
undo/redo writes the restored state back to storage.

By default every action earns its own undo step, which is rarely what a user means once a text box
is involved - typing "hello" into a bound property would record five steps. Pass a filter
(redux-undo's `filter`) to decide what counts:

```csharp
// Direct property assignments record as "Set X" - typically one per keystroke.
builder.Services.AddBlexHistory(filter: ctx => !ctx.ActionName.StartsWith("Set "));
```

A filtered action still applies and still moves the present forward; it simply doesn't become a
separate step, so the next undo reverts it together with the recorded action before it. Undo
therefore never lands on a state the user never saw. A filter that throws fails open - the action is
recorded anyway and the failure is reported through `OnError`.

A filter alone leaves the filtered work with no boundary of its own, so the next recorded action
swallows it. `History.Checkpoint(label)` commits the current state as an undo step on demand - call
it where the user expects a boundary (a pause in typing, a field losing focus, a wizard step).
Consecutive checkpoints with nothing in between collapse into one, so a debounced "user stopped
typing" timer can call it freely.

```csharp
private void OnBlur() => History.Checkpoint("Edit title");
```

## Resetting

`store.ResetState()` returns one store to the state it had when it was registered;
`manager.ResetAll()` does it across every registered store - the usual sign-out move. Each store
resets as its own normal, vetoable, observable, persisted and undoable `ResetState` action, so
middleware can exempt individual stores and DevTools shows one entry per store. A store already
holding its initial state is skipped entirely: resetting an untouched form notifies nobody, writes
nothing and adds no undo step.

```csharp
manager.ResetAll();

// Keep one store out of it:
builder.Services.AddBlex(options => options.UseFilter(
    ctx => !(ctx.Store.Name == "theme" && ctx.ActionName == "ResetState")));
```

## Testing

`Blex.Testing` provides a zero-setup harness that records dispatched actions:

```csharp
using var harness = TestHarnessBlex.For<CounterStore>();
harness.Store.Increment();
Assert.Equal(new[] { "Increment" }, harness.Log.Names);
Assert.Equal(1, harness.Snapshot()["Count"]!.GetValue<int>());

// Recorded actions include their argument payloads:
harness.Store.Add(5);
Assert.Equal(5, harness.Log.Last!.Args[0].Value);

// Await state conditions instead of sprinkling Task.Delay:
var load = harness.Store.LoadUser(42);
await harness.Store.WaitForAsync(() => !harness.Store.LoadUserIsLoading);
```

A store is rarely alone in production, so the harness can bring the pipeline with it - test the
store together with the guard rails that wrap it:

```csharp
using var harness = TestHarnessBlex.WithFilter<CounterStore>(_ => false);
harness.Store.Increment();
Assert.Equal(0, harness.Store.Count);   // vetoed
Assert.Empty(harness.Log.Actions);

using var audited = TestHarnessBlex.WithMiddleware<CounterStore>(new AuditMiddleware());
```

## Compile-time diagnostics

The generator validates store shapes and fails fast with precise errors instead of emitting broken
code: `BLEX001` store not partial · `BLEX002/003` underivable action/computed names ·
`BLEX004` computed with parameters · `BLEX005` generated-member collisions (including against
your own members and `StoreBaseBlex`) · `BLEX006` nested/generic/static stores · `BLEX007` non-async
effects · `BLEX008` static/readonly members · `BLEX009` `Latest` effect without a
`CancellationToken` (warning) · `BLEX010` `async void` actions · `BLEX011` discarded action
return values (warning) · `BLEX012` state field/property name conflicts · `BLEX013` by-ref
parameters · `BLEX014` generic action/effect methods · `BLEX015` record stores ·
`BLEX016` conflicting base class · `BLEX017` `void` computed methods · `BLEX018` a Blex member
attribute used outside a `[StoreAttributeBlex]` class (warning) - the one mistake that otherwise
compiles cleanly and silently generates nothing at all · `BLEX019` negative
`DebounceMilliseconds`.

## Time-travel debugging

1. Install the [Redux DevTools](https://github.com/reduxjs/redux-devtools) browser extension.
2. Run the app and open DevTools - you'll see an instance named after `DevToolsName`.
3. Every action streams in with its argument payload and the resulting state tree.
4. Use the slider / jump buttons to rewind and replay your application state live.

Under the hood the `Blex.Blazor` JS bridge talks to `window.__REDUX_DEVTOOLS_EXTENSION__`,
sends each action via `send(action, state)`, and applies `JUMP_TO_STATE` / `JUMP_TO_ACTION` /
`ROLLBACK` / `RESET` / `COMMIT` messages back onto the stores.

For production, set `<ProviderBlex EnableDevTools="false">` to disable the connection entirely, or
redact sensitive values from the monitor with sanitizers:

```csharp
builder.Services.AddBlex(options =>
{
    options.RedactDevToolsKeys("token", "password");      // replace matching keys with <redacted>
                                                          // (applies to action payloads too)
    options.DevToolsActionSanitizer = label => label;     // or rewrite action labels
});
```

`RedactDevToolsKeys` is additive and composes: calling it again *widens* the redaction set rather
than replacing it, and it wraps any `DevToolsStateSanitizer` assigned before it. Register redaction
last - assigning `DevToolsStateSanitizer` afterwards replaces it outright.

## .NET MAUI

All packages ship plain `net8.0`/`net9.0`/`net10.0` builds that resolve from every MAUI platform
target (`net8.0-android`, `net8.0-ios`, `net8.0-maccatalyst`, `net8.0-windows`) - no workloads
involved on the library side.

### Blazor Hybrid

MAUI Blazor Hybrid apps use `Blex` + `Blex.Blazor` exactly like any Blazor app: `AddBlex(...)` in
`MauiProgram.cs` and `<ProviderBlex>` wrapping the root component inside the `BlazorWebView`.
There is no browser extension inside a WebView, so the DevTools bridge detects its absence and
disables itself; set `<ProviderBlex EnableDevTools="false">` to skip loading it entirely.
`AddBlexLocalStoragePersistence()` works (the WebView provides `localStorage`), or add
`Blex.Maui` and call `AddBlexPreferencesPersistence()` to persist to OS-native app preferences
instead of the WebView profile.

### Native (XAML)

`Blex.Maui` makes stores first-class XAML citizens. Every store implements
`INotifyPropertyChanged` - raised with an empty property name ("all properties changed"), so
bindings to `[ComputedAttributeBlex]` properties stay fresh too - which means XAML can bind directly to
generated state, computed and effect-lifecycle properties:

```csharp
// MauiProgram.cs
builder.UseBlex();                          // manager + startup initializer
builder.AddBlexStore<CounterStore>();
builder.AddBlexPreferencesPersistence();    // OS-native Preferences storage
```

Register through the `MauiAppBuilder` extensions rather than `builder.Services`: a native MAUI app
has no DI scopes, so these register Blex as **singletons**, which is both the honest lifetime and
what keeps the wiring legal if you build the container with `ValidateScopes` enabled (it rejects
resolving a scoped service from the root provider - exactly what the startup initializer must do).
The core `builder.Services.AddBlexStore<T>()` still works and still defaults to scoped for Blazor;
pass `ServiceLifetime.Singleton` if you prefer to call it directly.

```xml
<ContentPage ... x:DataType="stores:CounterStore">
    <VerticalStackLayout>
        <Label Text="{Binding Count}" />
        <Label Text="{Binding DoubleCount}" />
    </VerticalStackLayout>
</ContentPage>
```

```csharp
public partial class MainPage : ContentPage
{
    private readonly CounterStore _store;

    public MainPage(CounterStore store)
    {
        InitializeComponent();
        BindingContext = _store = store;
    }

    private void OnIncrementClicked(object? sender, EventArgs e) => _store.Increment();
}
```

`UseBlex()` registers a startup initializer that mirrors what `<ProviderBlex>` does in Blazor:
when `MauiApp.Build()` runs it attaches every `AddBlexStore` store to the manager, rehydrates
persisted state from `Preferences`, and starts `HistoryBlex` recording (when registered) - all
before the first page appears. `AddBlexPreferencesPersistence()` supports the same
debounce/versioning/migration options as the browser-storage providers, and an unreadable
payload is reported through `options.OnError` and discarded rather than crashing startup.

To persist somewhere else (files, SQLite), register your own `IStorageBlex` before calling
`AddBlexPreferencesPersistence()` - the first registration wins, so give it the same lifetime as
the rest of your Blex registrations. An `IStorageBlex` that completes asynchronously should be
hydrated from app code (`await persistor.StartAsync()`) instead of relying on the synchronous
startup initializer.

If the startup initializer cannot resolve its services (typically a lifetime mismatch under
`ValidateScopes`), it reports through `options.OnError` and lets the app start rather than failing
`MauiApp.Build()`.

## Projects

| Project | Description |
|---|---|
| `src/Blex` | Core runtime (no JS dependency): `StoreBaseBlex`, attributes, dispatch, middleware, persistence, entity adapter, undo/redo, `ManagerBlex` manager. |
| `src/Blex.Generators` | Roslyn incremental source generator. |
| `src/Blex.Blazor` | Blazor integration: `ComponentBaseBlex`, `<ProviderBlex>`, browser-storage persistence, Redux DevTools bridge. |
| `src/Blex.Maui` | .NET MAUI integration: `UseBlex()` startup initializer, `Preferences`-backed persistence, XAML-bindable stores. |
| `src/Blex.Testing` | Test harness and assertions (`TestHarnessBlex`, `ActionLogBlex`). |
| `src/Demos/Blex.Demo` | Documentation website: every feature explained with a live, runnable demo. |
| `src/Demos/Blex.Sample` | Blazor WebAssembly demo (Counter, Todos, Weather). |
| `src/Tests/Blex.Tests` | xUnit tests for the runtime and generated code. |
| `src/Tests/Blex.Generators.Tests` | Generator-driver tests: all BLEX diagnostics + emission snapshots. |
| `src/Tests/Blex.Benchmarks` | BenchmarkDotNet suites (dispatch, fan-out, serialization, entity adapter). |

When consumed as a NuGet package, referencing `Blex` brings the generator automatically
(it is packed into `analyzers/dotnet/cs`). Inside this repo the sample/tests reference the
generator project directly as an analyzer.

## Build & test

```bash
dotnet build src/Blex.slnx
dotnet test src/Blex.slnx
dotnet run --project src/Demos/Blex.Demo     # documentation site
dotnet run --project src/Demos/Blex.Sample   # minimal sample app
dotnet pack src/Blex.slnx -c Release         # nupkg + snupkg into artifacts/packages
```

## Releasing

Package metadata (version, authors, tags, readme, icon, Source Link) is centralised in
[`src/Directory.Build.props`](https://github.com/msynk/Blex/blob/main/src/Directory.Build.props).
To publish:

1. Bump `<Version>` there and move the `[Unreleased]` section of
   [CHANGELOG.md](https://github.com/msynk/Blex/blob/main/CHANGELOG.md) under the new version heading.
2. Commit, then tag and push: `git tag v0.1.0 && git push origin v0.1.0`.
3. The [Release workflow](https://github.com/msynk/Blex/blob/main/.github/workflows/release.yml)
   verifies that the tag matches
   `<Version>`, builds, tests, packs, and pushes every package plus its symbol package to
   nuget.org using the `NUGET_API_KEY` repository secret.

`workflow_dispatch` runs the same pipeline in dry-run mode and uploads the packages as build
artifacts without publishing them.

## License

MIT
