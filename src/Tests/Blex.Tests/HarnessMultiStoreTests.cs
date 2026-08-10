using System.Threading.Tasks;
using Blex;
using Blex.Testing;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// The harness can bring more than one store, so cross-store coordination is testable without
/// hand-wiring a second manager -- the setup the harness exists to remove.
/// </summary>
public class HarnessMultiStoreTests
{
    [Fact]
    public void RegisteredStores_ShareTheHarnessManager()
    {
        using var harness = BlexTestHarness.For<CounterStore>();
        var profile = harness.Register(new ProfileStore());

        harness.Store.Increment();
        profile.SignIn("ada");

        Assert.Equal(new[] { "counter/Increment", "profile/SignIn" }, harness.Log.QualifiedNames);
        Assert.Equal("ada", harness.State["profile"]!["UserName"]!.GetValue<string>());
    }

    [Fact]
    public void CrossStoreReactions_WorkThroughTheHarness()
    {
        using var harness = BlexTestHarness.For<CounterStore>();
        var profile = harness.Register(new ProfileStore());

        using var sub = harness.Manager.SubscribeTo<ProfileStore>(_ => harness.Store.Reset());

        harness.Store.Add(7);
        Assert.Equal(7, harness.Store.Count);

        profile.SignOut();
        Assert.Equal(0, harness.Store.Count);
    }

    [Fact]
    public async Task DisposingTheHarness_QuiescesEveryRegisteredStore()
    {
        var failing = new FailingStore();
        Task watch;

        using (var harness = BlexTestHarness.For<CounterStore>())
        {
            harness.Register(failing);
            watch = failing.Watch();
            Assert.True(failing.WatchIsLoading);
        }

        await watch.WaitAsync(System.TimeSpan.FromSeconds(5));
        Assert.False(failing.WatchIsLoading);
    }
}
