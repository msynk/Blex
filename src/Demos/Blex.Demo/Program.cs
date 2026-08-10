using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Blex;
using Blex.Blazor;
using Blex.Demo;
using Blex.Demo.Services;
using Blex.Demo.Stores;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// --- Site services (not part of Blex; they power the live panels) --------------------------
builder.Services.AddScoped<ActionFeed>();
builder.Services.AddScoped<DemoGuard>();
builder.Services.AddScoped<ThemeService>();

// OnError is captured on the singleton options object, so the sink it writes to must outlive
// any single scope. In a Blazor Server app you would forward to ILogger instead.
var errorLog = new ErrorLog();
builder.Services.AddSingleton(errorLog);

// Same reasoning for the action-failure sink: OnActionError is captured on the singleton options.
var failureLog = new FailureLog();
builder.Services.AddSingleton(failureLog);

// --- Blex ----------------------------------------------------------------------------------
builder.Services.AddBlex(options =>
{
    options.DevToolsName = "Blex Docs";

    // Observing middleware, resolved from DI so it can take scoped dependencies.
    options.UseMiddleware<ActionFeedMiddleware>();

    // Veto middleware: cancels actions while the demo's read-only guard is on.
    options.UseMiddleware<GuardMiddleware>();

    // Display-only redaction: the settings store's Token never reaches the DevTools monitor.
    options.RedactDevToolsKeys("Token");

    // Every non-fatal failure Blex isolates from the pipeline lands here.
    options.OnError = errorLog.Add;

    // ...and every action body that throws lands here. Distinct on purpose: this one is the app's
    // own code failing, and the exception still propagates after the hook has seen it.
    options.OnActionError = failureLog.Add;
});

builder.Services.AddBlexStore<CounterStore>();
builder.Services.AddBlexStore<CartStore>();
builder.Services.AddBlexStore<EqualityDemoStore>();
builder.Services.AddBlexStore<EffectLabStore>();
builder.Services.AddBlexStore<SearchStore>();
builder.Services.AddBlexStore<ContactsStore>();
builder.Services.AddBlexStore<SettingsStore>();
builder.Services.AddBlexStore<OrdersStore>();
builder.Services.AddBlexStore<NotificationsStore>();

// Durable persistence for [BlexStoreAttribute(Persist = true)] stores, with debounced writes and a
// versioned envelope so the shape can evolve without stranding visitors on old payloads.
builder.Services.AddBlexLocalStoragePersistence(options =>
{
    options.KeyPrefix = "blex-docs:";
    options.DebounceInterval = TimeSpan.FromMilliseconds(300);
    options.DebounceMaxDelay = TimeSpan.FromSeconds(2);
    options.Version = 1;
    options.Migrate = (storeName, fromVersion, state) =>
    {
        // v0 payloads predate the versioned envelope; take them as-is.
        if (fromVersion == 0)
            return state;

        // Anything else is from a future build of the site: discard rather than guess.
        return null;
    };

    // Open this site in two tabs and change the theme in one: without this, whichever tab acted
    // last would silently win and the other would only catch up on its next reload.
    options.SyncAcrossTabs = true;
});

// In-app undo/redo across every store. <BlexProvider> calls Start() once rehydration finishes.
// No filter here: the docs demo is more useful when every action is its own step.
builder.Services.AddBlexHistory(maxEntries: 50);

await builder.Build().RunAsync();
