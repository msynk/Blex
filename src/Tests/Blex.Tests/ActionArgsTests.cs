using System.Collections.Generic;
using System.Text.Json.Nodes;
using Blex.Testing;
using Xunit;

namespace Blex.Tests;

public class ActionArgsTests
{
    private sealed class PayloadCapturingDevTools : IBlexDevTools
    {
        public List<(string Action, JsonObject? Payload)> Sent { get; } = [];
        public void Init(JsonObject globalState) { }
        public void Send(string actionName, JsonObject globalState) => Sent.Add((actionName, null));
        public void Send(string actionName, JsonObject globalState, JsonObject? payload) => Sent.Add((actionName, payload));
    }

    [Fact]
    public void ActionArgs_AreVisibleToSubscribers()
    {
        using var harness = BlexTestHarness.For<CounterStore>();
        harness.Store.Add(5);

        var recorded = harness.Log.Last!;
        var arg = Assert.Single(recorded.Args);
        Assert.Equal("amount", arg.Name);
        Assert.Equal(5, arg.Value);
    }

    [Fact]
    public void ParameterlessAction_HasEmptyArgs()
    {
        using var harness = BlexTestHarness.For<CounterStore>();
        harness.Store.Increment();

        Assert.Empty(harness.Log.Last!.Args);
    }

    [Fact]
    public void StandaloneSet_CarriesAssignedValueAsArg()
    {
        using var harness = BlexTestHarness.For<CounterStore>();
        harness.Store.Count = 3;

        var recorded = harness.Log.Last!;
        Assert.Equal("Set Count", recorded.ActionName);
        var arg = Assert.Single(recorded.Args);
        Assert.Equal("Count", arg.Name);
        Assert.Equal(3, arg.Value);
    }

    [Fact]
    public void ActionArgs_AreVisibleToPreActionFilters()
    {
        var store = new CounterStore();
        var manager = new BlexManager([new BlexFilterMiddleware(ctx =>
        {
            // Veto negative amounts based on the payload.
            foreach (var arg in ctx.Args)
            {
                if (arg.Value is int i && i < 0)
                    return false;
            }

            return true;
        })]);
        manager.Register(store);

        store.Add(-5);
        Assert.Equal(0, store.Count);

        store.Add(5);
        Assert.Equal(5, store.Count);
    }

    [Fact]
    public void DevTools_ReceivesActionPayload()
    {
        var store = new CounterStore();
        var manager = new BlexManager();
        manager.Register(store);
        var devTools = new PayloadCapturingDevTools();
        manager.ConnectDevTools(devTools);

        store.Add(7);

        var (action, payload) = Assert.Single(devTools.Sent);
        Assert.Equal("counter/Add", action);
        Assert.NotNull(payload);
        Assert.Equal(7, payload!["amount"]!.GetValue<int>());
    }

    [Fact]
    public void DevTools_PayloadIsRedacted_ByKeyRedaction()
    {
        var options = new BlexOptions();
        options.RedactDevToolsKeys("name");

        var store = new ProfileStore();
        var manager = new BlexManager { DevToolsStateSanitizer = options.DevToolsStateSanitizer };
        manager.Register(store);
        var devTools = new PayloadCapturingDevTools();
        manager.ConnectDevTools(devTools);

        store.SignIn("top-secret");

        var (_, payload) = Assert.Single(devTools.Sent);
        Assert.Equal("<redacted>", payload!["name"]!.GetValue<string>());
    }

    [Fact]
    public void LegacyDevToolsSink_StillReceivesActions_ViaDefaultInterfaceMethod()
    {
        var store = new CounterStore();
        var manager = new BlexManager();
        manager.Register(store);
        var devTools = new LegacySink();
        manager.ConnectDevTools(devTools);

        store.Add(2);

        Assert.Equal(["counter/Add"], devTools.Actions);
    }

    private sealed class LegacySink : IBlexDevTools
    {
        public List<string> Actions { get; } = [];
        public void Init(JsonObject globalState) { }
        public void Send(string actionName, JsonObject globalState) => Actions.Add(actionName);
    }
}
