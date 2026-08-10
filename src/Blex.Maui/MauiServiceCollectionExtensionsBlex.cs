using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Hosting;

namespace Blex.Maui;

/// <summary>DI helpers for wiring Blex into a .NET MAUI application.</summary>
/// <remarks>
/// <para>
/// A native MAUI app has no DI scopes: everything is resolved from the application's root provider
/// for the process lifetime. The <see cref="MauiAppBuilder"/> helpers here therefore register Blex
/// as <see cref="ServiceLifetime.Singleton"/> rather than the <see cref="ServiceLifetime.Scoped"/>
/// default that Blazor wants. Besides being the honest lifetime, it keeps the registrations valid
/// when the container is built with <c>ValidateScopes</c> enabled, which rejects resolving a scoped
/// service from the root provider -- something the startup initializer has to do.
/// </para>
/// <para>
/// Blazor Hybrid is the other way round: the <c>BlazorWebView</c> creates a scope, so those apps use
/// the core <c>AddBlex</c>/<c>AddBlexStore</c> and the <see cref="IServiceCollection"/> overload of
/// <see cref="AddBlexPreferencesPersistence(IServiceCollection, Action{PersistenceOptionsBlex}, ServiceLifetime)"/>,
/// all of which stay scoped by default.
/// </para>
/// </remarks>
public static class MauiServiceCollectionExtensionsBlex
{
    /// <summary>
    /// Registers the Blex manager and middleware (like <c>AddBlex</c>, but as singletons) plus the
    /// MAUI startup initializer that registers stores, rehydrates persisted state and starts
    /// history when the app is built. Register each store with
    /// <see cref="AddBlexStore{TStore}(MauiAppBuilder)"/> afterwards.
    /// </summary>
    /// <example><code>
    /// var builder = MauiApp.CreateBuilder();
    /// builder.UseBlex(options => options.DevToolsName = "My App");
    /// builder.AddBlexStore&lt;CounterStore&gt;();
    /// builder.AddBlexPreferencesPersistence();
    /// </code></example>
    public static MauiAppBuilder UseBlex(this MauiAppBuilder builder, Action<OptionsBlex>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddBlex(configure, ServiceLifetime.Singleton);
        builder.Services.AddBlexMauiInitializer();
        return builder;
    }

    /// <summary>
    /// Registers a store as a singleton -- the lifetime a scope-less MAUI host needs. Pairs with
    /// <see cref="UseBlex"/>.
    /// </summary>
    public static MauiAppBuilder AddBlexStore<TStore>(this MauiAppBuilder builder)
        where TStore : StoreBaseBlex
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddBlexStore<TStore>(ServiceLifetime.Singleton);
        return builder;
    }

    /// <summary>Registers in-app undo/redo as a singleton. Pairs with <see cref="UseBlex"/>.</summary>
    /// <param name="builder">The MAUI app builder.</param>
    /// <param name="maxEntries">Maximum number of undo entries retained.</param>
    /// <param name="filter">
    /// Optional predicate deciding which actions become their own undo step. See
    /// <see cref="HistoryBlex(ManagerBlex, int, Func{ActionContextBlex, bool})"/>.
    /// </param>
    public static MauiAppBuilder AddBlexHistory(
        this MauiAppBuilder builder,
        int maxEntries = 100,
        Func<ActionContextBlex, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddBlexHistory(maxEntries, ServiceLifetime.Singleton, filter);
        return builder;
    }

    /// <summary>
    /// Persists stores marked <c>[StoreAttributeBlex(Persist = true)]</c> to MAUI <c>Preferences</c> as a
    /// singleton. Pairs with <see cref="UseBlex"/>.
    /// </summary>
    public static MauiAppBuilder AddBlexPreferencesPersistence(
        this MauiAppBuilder builder,
        Action<PersistenceOptionsBlex>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddBlexPreferencesPersistence(configure, ServiceLifetime.Singleton);
        return builder;
    }

    /// <summary>
    /// Persists stores marked <c>[StoreAttributeBlex(Persist = true)]</c> to .NET MAUI
    /// <c>Preferences</c> (OS-native app settings). State is rehydrated automatically when
    /// <c>MauiApp.Build()</c> runs. Call after <c>UseBlex</c>/<c>AddBlex</c> and the store
    /// registrations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures <see cref="PersistenceOptionsBlex"/>.</param>
    /// <param name="lifetime">
    /// Lifetime of the persistor and the <c>Preferences</c> storage. Defaults to
    /// <see cref="ServiceLifetime.Scoped"/> so Blazor Hybrid keeps matching the rest of its
    /// (scoped) Blex registrations; the <see cref="MauiAppBuilder"/> overload passes
    /// <see cref="ServiceLifetime.Singleton"/> for native MAUI. Register your own
    /// <see cref="IStorageBlex"/> beforehand to override the storage -- the first registration
    /// wins, so give it the same lifetime.
    /// </param>
    public static IServiceCollection AddBlexPreferencesPersistence(
        this IServiceCollection services,
        Action<PersistenceOptionsBlex>? configure = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAdd(new ServiceDescriptor(typeof(IStorageBlex), typeof(PreferencesStorageBlex), lifetime));
        services.AddBlexPersistence(configure, lifetime);
        services.AddBlexMauiInitializer();
        return services;
    }

    /// <summary>
    /// Registers the startup initializer on its own -- useful when Blex was registered with the
    /// core <c>AddBlex</c> (rather than <see cref="UseBlex"/>) but store registration, hydration
    /// and history recording should still happen automatically at <c>MauiApp.Build()</c> time.
    /// Idempotent: calling it multiple times registers a single initializer.
    /// </summary>
    public static IServiceCollection AddBlexMauiInitializer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMauiInitializeService, MauiInitializerBlex>());
        return services;
    }
}
