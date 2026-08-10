using System.Threading.Tasks;
using Blex.Blazor;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// The browser-storage bridge: which module function each operation calls, and the watcher handle
/// that keeps two storage instances on one page from detaching each other's <c>storage</c> listener.
/// </summary>
public class BrowserStorageTests
{
    [Fact]
    public async Task Operations_TargetTheRequestedArea()
    {
        var js = new FakeJsRuntime();
        js.Module.Returns["get"] = "stored";
        await using var storage = new BlexBrowserStorage(js, BlexBrowserStorageKind.Session);

        var read = await storage.GetAsync("blex:settings");
        await storage.SetAsync("blex:settings", "{}");
        await storage.RemoveAsync("blex:settings");

        Assert.Equal("stored", read);
        Assert.Equal("./_content/Blex.Blazor/blex-storage.js", Assert.Single(js.Imports));
        Assert.Equal(["session", "blex:settings"], js.Module.ArgsOf("get"));
        Assert.Equal(["session", "blex:settings", "{}"], js.Module.ArgsOf("set"));
        Assert.Equal(["session", "blex:settings"], js.Module.ArgsOf("remove"));
    }

    [Fact]
    public async Task TheModuleIsImportedOnce()
    {
        var js = new FakeJsRuntime();
        await using var storage = new BlexBrowserStorage(js);

        await storage.SetAsync("a", "1");
        await storage.SetAsync("b", "2");
        await storage.GetAsync("a");

        Assert.Single(js.Imports);
    }

    [Fact]
    public async Task LocalStorage_WatchesAndUnwatchesByHandle()
    {
        var js = new FakeJsRuntime();
        js.Module.Returns["watch"] = 7;
        var storage = new BlexBrowserStorage(js, BlexBrowserStorageKind.Local);

        await storage.StartWatchingAsync();
        Assert.Equal("local", js.Module.ArgsOf("watch")![0]);

        await storage.DisposeAsync();

        // Unwatching by handle is what stops a second storage instance on the page from detaching
        // this one's listener; the module is shared, the handle is not.
        Assert.Equal([7], js.Module.ArgsOf("unwatch"));
        Assert.True(js.Module.Disposed);
    }

    [Fact]
    public async Task WatchingIsIdempotent()
    {
        var js = new FakeJsRuntime();
        js.Module.Returns["watch"] = 1;
        await using var storage = new BlexBrowserStorage(js);

        await storage.StartWatchingAsync();
        await storage.StartWatchingAsync();

        Assert.Equal(1, js.Module.CountOf("watch"));
    }

    [Fact]
    public async Task SessionStorage_NeverWatches()
    {
        // sessionStorage is per-tab, so there is no other instance to hear from.
        var js = new FakeJsRuntime();
        var storage = new BlexBrowserStorage(js, BlexBrowserStorageKind.Session);

        await storage.StartWatchingAsync();
        await storage.DisposeAsync();

        Assert.Equal(0, js.Module.CountOf("watch"));
        Assert.Equal(0, js.Module.CountOf("unwatch"));
    }

    [Fact]
    public async Task AZeroHandle_MeansNothingToUnwatch()
    {
        var js = new FakeJsRuntime();   // "watch" returns default(int) == 0
        var storage = new BlexBrowserStorage(js);

        await storage.StartWatchingAsync();
        await storage.DisposeAsync();

        Assert.Equal(1, js.Module.CountOf("watch"));
        Assert.Equal(0, js.Module.CountOf("unwatch"));
    }

    [Fact]
    public async Task ExternalChange_IsRaisedFromTheJsCallback()
    {
        var js = new FakeJsRuntime();
        js.Module.Returns["watch"] = 3;
        await using var storage = new BlexBrowserStorage(js);
        await storage.StartWatchingAsync();

        string? seen = "unset";
        storage.ExternalChange += key => seen = key;

        await storage.OnExternalChangeAsync("blex:settings");
        Assert.Equal("blex:settings", seen);

        // A null key means the whole area changed; it is forwarded as-is.
        await storage.OnExternalChangeAsync(null);
        Assert.Null(seen);
    }

    [Fact]
    public async Task DisposingWithoutEverTouchingStorage_IsANoOp()
    {
        var js = new FakeJsRuntime();
        var storage = new BlexBrowserStorage(js);

        await storage.DisposeAsync();

        Assert.Empty(js.Imports);
        Assert.False(js.Module.Disposed);
    }
}
