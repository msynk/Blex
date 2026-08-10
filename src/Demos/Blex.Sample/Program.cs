using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Blex;
using Blex.Blazor;
using Blex.Sample;
using Blex.Sample.Stores;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register Blex with a console-logging middleware, then add the stores.
builder.Services.AddBlex(options =>
{
    options.DevToolsName = "Blex Sample";
    options.UseMiddleware(ctx =>
        Console.WriteLine($"[blex] {ctx.QualifiedName} #{ctx.Sequence}"));
    // Demonstrate the DevTools state sanitizer (display-only redaction).
    options.RedactDevToolsKeys("secret");
    // Route isolated pipeline errors (persistence writes, throwing subscribers, ...) somewhere visible.
    options.OnError = error =>
        Console.Error.WriteLine($"[blex:{error.Source}] {error.Detail}: {error.Exception.Message}");
});
builder.Services.AddBlexStore<CounterStore>();
builder.Services.AddBlexStore<TodoStore>();
builder.Services.AddBlexStore<WeatherStore>();

// Persist [BlexStoreAttribute(Persist = true)] stores to localStorage (debounced so bursts of clicks
// coalesce into one write), and enable in-app undo/redo.
builder.Services.AddBlexLocalStoragePersistence(options =>
    options.DebounceInterval = TimeSpan.FromMilliseconds(300));
builder.Services.AddBlexHistory();

await builder.Build().RunAsync();
