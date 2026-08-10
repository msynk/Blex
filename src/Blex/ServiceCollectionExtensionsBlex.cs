using Microsoft.Extensions.DependencyInjection;

namespace Blex;

/// <summary>DI helpers for registering Blex and its stores.</summary>
/// <remarks>
/// Every registration defaults to <see cref="ServiceLifetime.Scoped"/>, which is what Blazor wants:
/// a Blazor Server circuit is a scope, so each user gets their own manager and stores. Hosts with
/// no scopes -- .NET MAUI, console apps, workers -- should pass
/// <see cref="ServiceLifetime.Singleton"/> instead (<c>Blex.Maui</c>'s <c>UseBlex()</c> does this
/// for you). Resolving a scoped service from the root provider works today but fails as soon as the
/// container is built with <c>ValidateScopes</c> enabled.
/// </remarks>
public static class ServiceCollectionExtensionsBlex
{
    /// <summary>
    /// Registers the Blex manager and middleware. Call <see cref="AddBlexStore{TStore}"/> for each store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures <see cref="OptionsBlex"/>.</param>
    /// <param name="lifetime">
    /// Lifetime of the manager and DI-resolved middleware. Defaults to
    /// <see cref="ServiceLifetime.Scoped"/> (Blazor); use <see cref="ServiceLifetime.Singleton"/>
    /// in hosts without scopes. Must match the lifetime used for the stores.
    /// </param>
    public static IServiceCollection AddBlex(
        this IServiceCollection services,
        Action<OptionsBlex>? configure = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new OptionsBlex();
        configure?.Invoke(options);
        services.AddSingleton(options);

        foreach (var type in options.MiddlewareTypes)
            services.Add(new ServiceDescriptor(typeof(IMiddlewareBlex), type, lifetime));

        services.Add(new ServiceDescriptor(typeof(ManagerBlex), sp =>
        {
            var opts = sp.GetRequiredService<OptionsBlex>();
            var resolved = sp.GetServices<IMiddlewareBlex>().ToList();
            resolved.AddRange(opts.MiddlewareInstances);
            return new ManagerBlex(resolved)
            {
                DevToolsStateSanitizer = opts.DevToolsStateSanitizer,
                DevToolsActionSanitizer = opts.DevToolsActionSanitizer,
                OnError = opts.OnError,
            };
        }, lifetime));

        return services;
    }

    /// <summary>
    /// Registers a store exposed both as its concrete type and as <see cref="IStoreBlex"/>.
    /// The store is attached to the <see cref="ManagerBlex"/> on first resolution, so actions are
    /// observed even in hosts without a <c>&lt;ProviderBlex&gt;</c> (console apps, workers, tests).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">
    /// Defaults to <see cref="ServiceLifetime.Scoped"/> (Blazor); use
    /// <see cref="ServiceLifetime.Singleton"/> in hosts without scopes. Must match the lifetime
    /// passed to <see cref="AddBlex"/>.
    /// </param>
    public static IServiceCollection AddBlexStore<TStore>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TStore : StoreBaseBlex
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Add(new ServiceDescriptor(typeof(TStore), sp =>
        {
            var store = ActivatorUtilities.CreateInstance<TStore>(sp);
            sp.GetRequiredService<ManagerBlex>().Register(store);
            return store;
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(IStoreBlex), sp => sp.GetRequiredService<TStore>(), lifetime));
        return services;
    }

    /// <summary>
    /// Enables automatic persistence for stores marked <c>[StoreAttributeBlex(Persist = true)]</c>. Requires an
    /// <see cref="IStorageBlex"/> to be registered (for Blazor use <c>AddBlexLocalStoragePersistence</c>
    /// or <c>AddBlexSessionStoragePersistence</c> from <c>Blex.Blazor</c>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures <see cref="PersistenceOptionsBlex"/>.</param>
    /// <param name="lifetime">
    /// Defaults to <see cref="ServiceLifetime.Scoped"/>; must match the lifetime passed to
    /// <see cref="AddBlex"/>.
    /// </param>
    public static IServiceCollection AddBlexPersistence(
        this IServiceCollection services,
        Action<PersistenceOptionsBlex>? configure = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new PersistenceOptionsBlex();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.Add(new ServiceDescriptor(typeof(StatePersistorBlex), sp => new StatePersistorBlex(
            sp.GetRequiredService<ManagerBlex>(),
            sp.GetRequiredService<IStorageBlex>(),
            sp.GetRequiredService<PersistenceOptionsBlex>()), lifetime));
        return services;
    }

    /// <summary>
    /// Registers in-app undo/redo (<see cref="HistoryBlex"/>). Resolve <see cref="HistoryBlex"/>
    /// to call <c>Undo()</c>/<c>Redo()</c>; <c>&lt;ProviderBlex&gt;</c> starts recording automatically.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="maxEntries">Maximum number of undo entries retained.</param>
    /// <param name="lifetime">
    /// Defaults to <see cref="ServiceLifetime.Scoped"/>; must match the lifetime passed to
    /// <see cref="AddBlex"/>.
    /// </param>
    /// <param name="filter">
    /// Optional predicate deciding which actions become their own undo step. See
    /// <see cref="HistoryBlex(ManagerBlex, int, Func{ActionContextBlex, bool})"/>.
    /// </param>
    public static IServiceCollection AddBlexHistory(
        this IServiceCollection services,
        int maxEntries = 100,
        ServiceLifetime lifetime = ServiceLifetime.Scoped,
        Func<ActionContextBlex, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Add(new ServiceDescriptor(
            typeof(HistoryBlex),
            sp => new HistoryBlex(sp.GetRequiredService<ManagerBlex>(), maxEntries, filter),
            lifetime));
        return services;
    }
}
