using System.Collections.Generic;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// Store names key the global state tree, the DevTools slices and the persistence storage keys, so
/// a collision silently shadows one store with another. The manager reports it instead of failing
/// silently (or throwing, which would take down app startup).
/// </summary>
public class DuplicateStoreNameTests
{
    [Fact]
    public void RegisteringTwoStoresWithTheSameName_ReportsAnError()
    {
        var errors = new List<BlexError>();
        var manager = new BlexManager { OnError = errors.Add };

        manager.Register(new CounterStore());
        manager.Register(new CounterStore()); // both are named "counter"

        var error = Assert.Single(errors);
        Assert.Equal("register", error.Source);
        Assert.Contains("counter", error.Detail);
        Assert.Equal(2, manager.Stores.Count); // still registered; the app keeps working
    }

    [Fact]
    public void RegisteringTheSameInstanceTwice_IsSilentlyIdempotent()
    {
        var errors = new List<BlexError>();
        var manager = new BlexManager { OnError = errors.Add };
        var store = new CounterStore();

        manager.Register(store);
        manager.Register(store);

        Assert.Empty(errors);
        Assert.Single(manager.Stores);
    }

    [Fact]
    public void DistinctNames_ReportNothing()
    {
        var errors = new List<BlexError>();
        var manager = new BlexManager { OnError = errors.Add };

        manager.Register(new CounterStore());
        manager.Register(new SettingsStore());

        Assert.Empty(errors);
    }

    [Fact]
    public void ReRegisteringAfterUnregister_ReportsNothing()
    {
        var errors = new List<BlexError>();
        var manager = new BlexManager { OnError = errors.Add };
        var first = new CounterStore();

        manager.Register(first);
        manager.Unregister(first);
        manager.Register(new CounterStore());

        Assert.Empty(errors);
    }
}
