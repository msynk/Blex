using System.Collections.Generic;
using System.Threading.Tasks;
using Blex;
using Xunit;

namespace Blex.Tests;

public class EffectTests
{
    [Fact]
    public async Task Effect_SetsLoadingDuringRun_AndClearsAfter()
    {
        var store = new DataStore();
        Assert.False(store.LoadIsLoading);

        var task = store.Load("hello");
        Assert.True(store.LoadIsLoading); // loading set synchronously before first await

        await task;
        Assert.False(store.LoadIsLoading);
        Assert.Equal("hello", store.Value);
        Assert.Null(store.LoadError);
    }

    [Fact]
    public async Task Effect_CapturesException_IntoError_WithoutThrowing()
    {
        var store = new DataStore { ShouldThrow = true };

        await store.Load("x"); // must not throw

        Assert.NotNull(store.LoadError);
        Assert.IsType<System.InvalidOperationException>(store.LoadError);
        Assert.False(store.LoadIsLoading);
    }

    [Fact]
    public async Task Effect_ClearsPreviousError_OnRerun()
    {
        var store = new DataStore { ShouldThrow = true };
        await store.Load("x");
        Assert.NotNull(store.LoadError);

        store.ShouldThrow = false;
        await store.Load("ok");

        Assert.Null(store.LoadError);
        Assert.Equal("ok", store.Value);
    }

    [Fact]
    public async Task Effect_RecordsExactlyOneAction()
    {
        var seen = new List<string>();
        var manager = new BlexManager(new[] { new BlexDelegateMiddleware(c => seen.Add(c.ActionName)) });
        var store = new DataStore();
        manager.Register(store);

        await store.Load("hello");

        Assert.Equal(new[] { "Load" }, seen); // loading/error toggles are not recorded
    }

    [Fact]
    public async Task Effect_NotifiesOnLoadingTransition()
    {
        var store = new DataStore();
        var raised = 0;
        store.StateChanged += () => raised++;

        await store.Load("hello");

        Assert.True(raised >= 2); // at least loading=true and loading=false
    }
}
