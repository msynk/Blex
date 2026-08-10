using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blex;
using Blex.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// Exercises Blex.Maui end-to-end without a device: a fake <see cref="IPreferences"/> stands in
/// for the platform store, and the startup initializer is driven the way MauiApp.Build() would.
/// </summary>
public class MauiIntegrationTests
{
    private sealed class FakePreferences : IPreferences
    {
        public Dictionary<string, object?> Data { get; } = new();

        public bool ContainsKey(string key, string? sharedName = null) => Data.ContainsKey(key);

        public void Remove(string key, string? sharedName = null) => Data.Remove(key);

        public void Clear(string? sharedName = null) => Data.Clear();

        public void Set<T>(string key, T value, string? sharedName = null) => Data[key] = value;

        public T Get<T>(string key, T defaultValue, string? sharedName = null)
            => Data.TryGetValue(key, out var value) ? (T)value! : defaultValue;
    }

    private static ServiceProvider BuildProvider(FakePreferences preferences, bool history = false)
    {
        var services = new ServiceCollection();
        services.AddBlex();
        services.AddBlexStore<SettingsStore>();
        if (history)
            services.AddBlexHistory();
        services.AddScoped<IBlexStorage>(_ => new BlexPreferencesStorage(preferences));
        services.AddBlexPreferencesPersistence();
        return services.BuildServiceProvider();
    }

    private static void RunInitializers(ServiceProvider provider)
    {
        foreach (var initializer in provider.GetServices<IMauiInitializeService>())
            initializer.Initialize(provider);
    }

    [Fact]
    public void MauiRegistrations_AreValidUnderScopeValidation()
    {
        // A MAUI app has no scopes: everything resolves from the root provider, and the startup
        // initializer has to do exactly that. Registering Blex as scoped makes it illegal the
        // moment the container is built with ValidateScopes, so the MAUI helpers use singletons.
        var services = new ServiceCollection();
        services.AddBlex(lifetime: ServiceLifetime.Singleton);
        services.AddBlexStore<SettingsStore>(ServiceLifetime.Singleton);
        services.AddBlexHistory(lifetime: ServiceLifetime.Singleton);
        services.AddScoped(_ => new FakePreferences());
        services.AddSingleton<IBlexStorage>(new BlexPreferencesStorage(new FakePreferences()));
        services.AddBlexPreferencesPersistence(lifetime: ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        RunInitializers(provider); // must not throw
        Assert.Equal("light", provider.GetRequiredService<SettingsStore>().Theme);

        // Singleton registration also means the host may tear the container down synchronously,
        // which ServiceProvider rejects for an IAsyncDisposable-only service.
        provider.Dispose();
    }

    [Fact]
    public void SingletonPersistor_SurvivesSynchronousContainerDisposal()
    {
        var preferences = new FakePreferences();
        var services = new ServiceCollection();
        services.AddBlex(lifetime: ServiceLifetime.Singleton);
        services.AddBlexStore<SettingsStore>(ServiceLifetime.Singleton);
        services.AddSingleton<IBlexStorage>(new BlexPreferencesStorage(preferences));
        services.AddBlexPreferencesPersistence(lifetime: ServiceLifetime.Singleton);

        var provider = services.BuildServiceProvider();
        RunInitializers(provider);
        provider.GetRequiredService<SettingsStore>().SetTheme("blue");

        provider.Dispose(); // must not throw...

        // ...and must have handed the pending state to storage on the way out.
        Assert.Contains("blue", (string)preferences.Data["blex:settings"]!);
    }

    [Fact]
    public void ScopedRegistrations_UnderScopeValidation_ReportRatherThanCrashStartup()
    {
        // The legacy (core, scoped) wiring is still rejected by ValidateScopes. Startup must
        // survive it with an actionable report instead of taking the app down.
        var errors = new List<BlexError>();
        var services = new ServiceCollection();
        services.AddBlex(options => options.OnError = errors.Add, ServiceLifetime.Singleton);
        services.AddBlexStore<SettingsStore>(); // scoped: the mismatch under test
        services.AddBlexMauiInitializer();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        RunInitializers(provider); // must not throw

        Assert.Contains(errors, e => e.Source == "startup");
    }

    [Fact]
    public async Task PreferencesStorage_RoundTrips()
    {
        var storage = new BlexPreferencesStorage(new FakePreferences());

        Assert.Null(await storage.GetAsync("k"));

        await storage.SetAsync("k", "v");
        Assert.Equal("v", await storage.GetAsync("k"));

        await storage.RemoveAsync("k");
        Assert.Null(await storage.GetAsync("k"));
    }

    [Fact]
    public async Task Initializer_RegistersStores_AndHydratesPersistedState()
    {
        var preferences = new FakePreferences();
        preferences.Data["blex:settings"] = "{\"Theme\":\"dark\",\"FontSize\":20}";

        await using var provider = BuildProvider(preferences);
        RunInitializers(provider);

        var settings = provider.GetRequiredService<SettingsStore>();
        Assert.Equal("dark", settings.Theme);
        Assert.Equal(20, settings.FontSize);
    }

    [Fact]
    public async Task Actions_AfterInitialization_ArePersistedToPreferences()
    {
        var preferences = new FakePreferences();
        await using var provider = BuildProvider(preferences);
        RunInitializers(provider);

        provider.GetRequiredService<SettingsStore>().SetTheme("blue");
        await provider.GetRequiredService<BlexStatePersistor>().FlushAsync();

        Assert.Contains("blue", (string)preferences.Data["blex:settings"]!);
    }

    [Fact]
    public async Task Initializer_StartsHistory_AfterHydration()
    {
        var preferences = new FakePreferences();
        preferences.Data["blex:settings"] = "{\"Theme\":\"dark\",\"FontSize\":12}";

        await using var provider = BuildProvider(preferences, history: true);
        RunInitializers(provider);

        var settings = provider.GetRequiredService<SettingsStore>();
        var historian = provider.GetRequiredService<BlexHistory>();
        Assert.False(historian.CanUndo);

        settings.SetTheme("light");
        Assert.True(historian.CanUndo);

        historian.Undo();
        // The undo baseline is the hydrated state, not the store's compiled-in default.
        Assert.Equal("dark", settings.Theme);
    }

    [Fact]
    public void Initializer_IsRegisteredOnce_AcrossRepeatedCalls()
    {
        var services = new ServiceCollection();
        services.AddBlex();
        services.AddBlexPreferencesPersistence();
        services.AddBlexPreferencesPersistence();
        services.AddBlexMauiInitializer();

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<IMauiInitializeService>());
    }

    [Fact]
    public async Task Initializer_UnreadableStorage_DoesNotThrow_AndReportsError()
    {
        var preferences = new FakePreferences();
        // A non-string value makes BlexPreferencesStorage's Get<string?> cast throw.
        preferences.Data["blex:settings"] = 42;

        var errors = new List<BlexError>();
        var services = new ServiceCollection();
        services.AddBlex(options => options.OnError = errors.Add);
        services.AddBlexStore<SettingsStore>();
        services.AddScoped<IBlexStorage>(_ => new BlexPreferencesStorage(preferences));
        services.AddBlexPreferencesPersistence();

        await using var provider = services.BuildServiceProvider();
        RunInitializers(provider); // must not throw: the app still starts with default state

        Assert.Equal("light", provider.GetRequiredService<SettingsStore>().Theme);
        Assert.Contains(errors, e => e.Source == "persistence");
    }
}
