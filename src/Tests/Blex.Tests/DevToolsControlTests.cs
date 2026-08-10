using System.Collections.Generic;
using System.Text.Json.Nodes;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// The monitor's recording controls: "Pause recording" stops the timeline without freezing the app,
/// "Lock changes" freezes the app while leaving time-travel working.
/// </summary>
public class DevToolsControlTests
{
    private sealed class FakeDevTools : IBlexDevTools
    {
        public List<string> Actions { get; } = [];
        public int Inits { get; private set; }

        public void Init(JsonObject globalState) => Inits++;

        public void Send(string actionName, JsonObject globalState) => Actions.Add(actionName);

        public void Send(string actionName, JsonObject globalState, JsonObject? payload) => Actions.Add(actionName);
    }

    private static (BlexManager Manager, CounterStore Store, FakeDevTools DevTools) Setup()
    {
        var manager = new BlexManager();
        var store = new CounterStore();
        manager.Register(store);
        var devTools = new FakeDevTools();
        manager.ConnectDevTools(devTools);
        return (manager, store, devTools);
    }

    private static string Dispatch(string payloadType, bool status)
        => "{\"type\":\"DISPATCH\",\"payload\":{\"type\":\"" + payloadType + "\",\"status\":" + (status ? "true" : "false") + "}}";

    [Fact]
    public void PauseRecording_StopsTheTimelineButNotTheApp()
    {
        var (manager, store, devTools) = Setup();

        store.Increment();
        Assert.Single(devTools.Actions);

        manager.HandleDevToolsMessage(Dispatch("PAUSE_RECORDING", true));
        Assert.True(manager.IsDevToolsPaused);

        store.Increment();
        Assert.Equal(2, store.Count);        // the action still applied...
        Assert.Single(devTools.Actions);     // ...and was not streamed

        manager.HandleDevToolsMessage(Dispatch("PAUSE_RECORDING", false));
        store.Increment();
        Assert.Equal(2, devTools.Actions.Count);
    }

    [Fact]
    public void PauseRecording_StillReachesMiddlewareAndSubscribers()
    {
        var (manager, store, _) = Setup();
        var seen = new List<string>();
        using var sub = manager.Subscribe(ctx => seen.Add(ctx.ActionName));

        manager.HandleDevToolsMessage(Dispatch("PAUSE_RECORDING", true));
        store.Increment();

        Assert.Equal(new[] { "Increment" }, seen);
    }

    [Fact]
    public void LockChanges_VetoesEveryAction()
    {
        var (manager, store, _) = Setup();

        manager.HandleDevToolsMessage(Dispatch("LOCK_CHANGES", true));
        Assert.True(manager.AreChangesLocked);

        store.Increment();
        store.Count = 12;
        Assert.Equal(0, store.Count);

        manager.HandleDevToolsMessage(Dispatch("LOCK_CHANGES", false));
        store.Increment();
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void LockChanges_LeavesTimeTravelWorking()
    {
        var (manager, store, _) = Setup();
        store.Increment();
        store.Increment();
        var snapshot = manager.CaptureGlobalState().ToJsonString();
        store.Increment();

        manager.HandleDevToolsMessage(Dispatch("LOCK_CHANGES", true));
        manager.HandleDevToolsMessage(
            $$"""{"type":"DISPATCH","payload":{"type":"JUMP_TO_STATE"},"state":{{JsonValue.Create(snapshot)!.ToJsonString()}}}""");

        Assert.Equal(2, store.Count);   // the slider still moves while the app is frozen
    }

    [Fact]
    public void AMissingStatus_TogglesInstead()
    {
        var (manager, _, _) = Setup();

        manager.HandleDevToolsMessage("""{"type":"DISPATCH","payload":{"type":"PAUSE_RECORDING"}}""");
        Assert.True(manager.IsDevToolsPaused);

        manager.HandleDevToolsMessage("""{"type":"DISPATCH","payload":{"type":"PAUSE_RECORDING"}}""");
        Assert.False(manager.IsDevToolsPaused);
    }

    [Fact]
    public void DisconnectingClearsTheLock()
    {
        var (manager, store, _) = Setup();
        manager.HandleDevToolsMessage(Dispatch("LOCK_CHANGES", true));

        // A monitor closed (or a circuit torn down) while locked would otherwise leave the app
        // frozen with no way left to unfreeze it.
        manager.DisconnectDevTools();

        Assert.False(manager.AreChangesLocked);
        store.Increment();
        Assert.Equal(1, store.Count);
    }
}
