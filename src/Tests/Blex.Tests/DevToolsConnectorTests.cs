using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blex.Blazor;
using Microsoft.JSInterop;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// The Redux DevTools bridge: it must connect only when the extension answered, stream actions with
/// their payloads, survive an absent extension, and disconnect exactly its own connection.
/// </summary>
public class DevToolsConnectorTests
{
    private static (FakeJsRuntime Js, BlexManager Manager, CounterStore Store) Setup(int handle)
    {
        var js = new FakeJsRuntime();
        js.Module.Returns["connect"] = handle;
        var manager = new BlexManager();
        var store = new CounterStore();
        manager.Register(store);
        return (js, manager, store);
    }

    [Fact]
    public async Task Connecting_SendsTheInitialSnapshot()
    {
        var (js, manager, _) = Setup(handle: 4);
        await using var connector = new BlexReduxDevToolsConnector(js);

        await connector.ConnectAsync(manager, "My App");

        Assert.Equal("./_content/Blex.Blazor/blex-devtools.js", Assert.Single(js.Imports));
        Assert.Equal("My App", js.Module.ArgsOf("connect")![1]);

        var init = js.Module.ArgsOf("init");
        Assert.NotNull(init);
        Assert.Equal(4, init![0]);
        Assert.Contains("\"counter\"", (string)init[1]!);
    }

    [Fact]
    public async Task EachActionIsStreamedWithItsPayload()
    {
        var (js, manager, store) = Setup(handle: 1);
        await using var connector = new BlexReduxDevToolsConnector(js);
        await connector.ConnectAsync(manager, "Blex");

        store.Add(5);

        var send = js.Module.ArgsOf("send");
        Assert.NotNull(send);
        Assert.Equal(1, send![0]);
        Assert.Equal("counter/Add", send[1]);
        Assert.Contains("\"Count\":5", (string)send[2]!);
        Assert.Contains("\"amount\":5", (string)send[3]!);
    }

    [Fact]
    public async Task AParameterlessAction_SendsNoPayload()
    {
        var (js, manager, store) = Setup(handle: 1);
        await using var connector = new BlexReduxDevToolsConnector(js);
        await connector.ConnectAsync(manager, "Blex");

        store.Increment();

        Assert.Null(js.Module.ArgsOf("send")![3]);
    }

    [Fact]
    public async Task NoExtension_MeansNoConnectionAndNoStreaming()
    {
        // connect() returns 0 when window.__REDUX_DEVTOOLS_EXTENSION__ is absent.
        var (js, manager, store) = Setup(handle: 0);
        await using var connector = new BlexReduxDevToolsConnector(js);

        await connector.ConnectAsync(manager, "Blex");
        store.Increment();

        Assert.Equal(0, js.Module.CountOf("init"));
        Assert.Equal(0, js.Module.CountOf("send"));
        Assert.Equal(1, store.Count);   // the app is entirely unaffected
    }

    [Fact]
    public async Task AFailedImport_IsReportedAndDoesNotBreakStartup()
    {
        var js = new FakeJsRuntime { ImportFailure = () => new JSException("module not found") };
        var manager = new BlexManager();
        var errors = new List<BlexError>();
        manager.OnError = errors.Add;
        var store = new CounterStore();
        manager.Register(store);

        await using var connector = new BlexReduxDevToolsConnector(js);
        await connector.ConnectAsync(manager, "Blex");

        store.Increment();

        Assert.Equal(1, store.Count);
        Assert.Contains(errors, e => e.Source == "devtools");
    }

    [Fact]
    public async Task ADisconnectedCircuitDuringImport_IsSwallowed()
    {
        var js = new FakeJsRuntime { ImportFailure = () => new JSDisconnectedException("gone") };
        var manager = new BlexManager();
        var errors = new List<BlexError>();
        manager.OnError = errors.Add;

        await using var connector = new BlexReduxDevToolsConnector(js);
        await connector.ConnectAsync(manager, "Blex");

        Assert.Empty(errors);
    }

    [Fact]
    public async Task DisposingDetachesOnlyThisConnection()
    {
        var (js, manager, _) = Setup(handle: 9);
        var connector = new BlexReduxDevToolsConnector(js);
        await connector.ConnectAsync(manager, "Blex");

        await connector.DisposeAsync();

        // By handle: the JS module is shared by every connector on the page, so a bare
        // ext.disconnect() would tear down whichever connection happened to be last.
        Assert.Equal([9], js.Module.ArgsOf("disconnect"));
        Assert.True(js.Module.Disposed);
    }

    [Fact]
    public async Task MessagesFromTheExtension_DriveTimeTravel()
    {
        var (js, manager, store) = Setup(handle: 2);
        await using var connector = new BlexReduxDevToolsConnector(js);
        await connector.ConnectAsync(manager, "Blex");

        store.Increment();
        var snapshot = manager.CaptureGlobalState().ToJsonString();
        store.Increment();
        Assert.Equal(2, store.Count);

        await connector.HandleMessage(
            $$"""{"type":"DISPATCH","payload":{"type":"JUMP_TO_STATE"},"state":{{System.Text.Json.Nodes.JsonValue.Create(snapshot)!.ToJsonString()}}}""");

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task DisposingBeforeTheImportResolves_DoesNotConnect()
    {
        var (js, manager, store) = Setup(handle: 5);
        var connector = new BlexReduxDevToolsConnector(js);

        await connector.DisposeAsync();
        await connector.ConnectAsync(manager, "Blex");

        store.Increment();
        Assert.Equal(0, js.Module.CountOf("send"));
    }
}
