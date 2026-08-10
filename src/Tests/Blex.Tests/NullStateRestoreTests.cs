using System.Text.Json.Nodes;
using Xunit;

namespace Blex.Tests;

public class NullStateRestoreTests
{
    [Fact]
    public void Restore_SetsReferenceField_BackToNull()
    {
        var store = new ProfileStore();
        store.SignOut(); // UserName = null
        var snapshot = store.SerializeState();

        store.SignIn("alice");
        Assert.Equal("alice", store.UserName);

        store.RestoreState(snapshot);
        Assert.Null(store.UserName); // a JSON-null property must restore to null
    }

    [Fact]
    public void Restore_MissingProperty_KeepsCurrentValue()
    {
        var store = new ProfileStore();
        store.SignIn("alice");

        store.RestoreState(new JsonObject { ["Age"] = 30 });

        Assert.Equal("alice", store.UserName); // absent from snapshot -> untouched
        Assert.Equal(30, store.Age);
    }

    [Fact]
    public void TimeTravel_RoundTripsNullValues_AcrossManager()
    {
        var store = new ProfileStore();
        var manager = new BlexManager();
        manager.Register(store);

        store.SignOut();
        var nullState = manager.CaptureGlobalState();

        store.SignIn("bob");
        manager.RestoreGlobalState(nullState);

        Assert.Null(store.UserName);
    }
}
