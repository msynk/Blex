# Blex.Demo

The Blex documentation website: a Blazor WebAssembly app that documents every feature of the
library and demonstrates each one with a live, runnable example on the same page.

It is deliberately self-hosting - the site is itself a Blex app, so the **Inspector** dock at the
bottom of every page shows the real action feed, the real serialized state tree, the real undo stack
and the real `OnError` sink. Nothing on the site is mocked.

## Running it

```bash
dotnet run --project src/Demos/Blex.Demo
```

Then open <http://localhost:5280>.

## What is covered

| Page | Feature |
|---|---|
| Introduction | Positioning, comparison table, the whole-store sample |
| Installation | Packages, DI registration, `<BlexProvider>` parameters, headless hosts |
| Quick start | A working counter in three files |
| Stores & state | `[BlexStoreAttribute]`, `[BlexStateAttribute]`, naming, shape rules, change detection, `BlexStoreBase` API |
| Computed state | `[BlexComputedAttribute]`, memoization, composition, invalidation semantics |
| Actions | `[BlexActionAttribute]`, naming, payloads, batching, `Set X`, `Batch`, `ResetState`, async actions |
| Effects | `[BlexEffectAttribute]`, loading/error, cancellation, all four concurrency modes |
| Components & selectors | `BlexComponentBase`, selector subscriptions, `OwnsSubscription` |
| Cross-store coordination | `BlexManager` registry, subscriptions, action context |
| Entity adapter | `BlexEntityState`, `BlexEntityAdapter`, sorting, every CRUD operation |
| Middleware | Observing, vetoing, registration modes, error isolation |
| Error isolation | `BlexError`, `OnError`, every reported source |
| Persistence | `Persist`, providers, debounce, versioning, migrations, `BlexStatePersistor` |
| Undo & redo | `BlexHistory` |
| DevTools & time travel | The bridge, wire format, time-travel messages, sanitizers |
| Testing | `BlexTestHarness`, `BlexActionLog`, `WaitForAsync`, `CountNotifications` |
| Diagnostics | All sixteen `BLEX####` codes, each with a rejected and a fixed sample |
| API reference | Every public type, grouped by namespace |

## No third-party dependencies

The site references only `Blex`, `Blex.Blazor` and the Blazor WebAssembly packages. There is no
CSS framework, no CDN and no JavaScript library:

- **Syntax highlighting** is [`Components/CodeHighlighter.cs`](Components/CodeHighlighter.cs), a
  scanner for C#, Razor, XML, JSON and shell.
- **Styling** is one hand-written stylesheet, [`wwwroot/css/site.css`](wwwroot/css/site.css), with
  light and dark themes driven by CSS custom properties.
- **JavaScript** is [`wwwroot/js/site.js`](wwwroot/js/site.js) - clipboard, theme persistence and a
  `localStorage` read for the persistence demo. That is all of it.

This keeps the published output small and lets the site work offline and under a strict CSP.

## Adding a page

The site map lives in one place, [`DocsNav.cs`](DocsNav.cs). Add a `DocEntry` to the right
`DocSection` and the sidebar, the previous/next footer links and the introduction's contents grid
all pick it up. Then create the page:

```razor
@page "/my-topic"

<DocPage Href="my-topic" Title="My topic" Lead="One sentence of context.">
    <h2>A heading</h2>
    <p>Prose.</p>

    <CodeBlock Code="@Sample" FileName="Example.cs" />

    <DemoCard Title="Live demo" Description="What to try." Source="@Sample">
        @* interactive markup *@
    </DemoCard>
</DocPage>

@code {
    private const string Sample = """
        // C# here
        """;
}
```

Snippets are C# raw string literals in the `@code` block, which keeps them copy-pasteable and free
of Razor escaping.

## Publishing

The site is a static Blazor WebAssembly app, so any static host will serve it.

```bash
dotnet publish src/Demos/Blex.Demo -c Release -o publish
# the deployable site is publish/wwwroot
```

[`.github/workflows/docs.yml`](../../../.github/workflows/docs.yml) publishes to GitHub Pages on
every push to `main`. It handles the two things a project page needs:

1. **Base href** - rewrites `<base href="/" />` to `<base href="/<repo>/" />`, since the site is
   served from a subpath.
2. **SPA fallback** - copies `index.html` to `404.html`, because GitHub Pages has no rewrite rule
   and would otherwise 404 on a deep link such as `/Blex/effects`.

`wwwroot/.nojekyll` is committed so Pages does not strip the `_framework` directory.

To publish somewhere else, set the base href to wherever the app is mounted and point the host's
fallback at `index.html`.
