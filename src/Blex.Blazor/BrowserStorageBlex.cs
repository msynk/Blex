using Microsoft.JSInterop;

namespace Blex.Blazor;

/// <summary>
/// <see cref="IStorageBlex"/> backed by the browser's <c>localStorage</c>/<c>sessionStorage</c>
/// via a tiny JS bridge. Used by Blex persistence to survive page reloads in Blazor WebAssembly.
/// </summary>
public sealed class BrowserStorageBlex : IStorageBlex, IExternalStorageChangeBlex, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly string _area;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BrowserStorageBlex>? _self;
    private bool _watching;

    /// <summary>Creates a storage instance over the given Web Storage area.</summary>
    public BrowserStorageBlex(IJSRuntime js, BrowserStorageKindBlex kind = BrowserStorageKindBlex.Local)
    {
        _js = js;
        _area = kind == BrowserStorageKindBlex.Session ? "session" : "local";
    }

    /// <inheritdoc />
    /// <remarks>
    /// Backed by the browser's <c>storage</c> event, which fires only in the tabs that did
    /// <em>not</em> write the value -- so this never echoes back this instance's own writes.
    /// </remarks>
    public event Action<string?>? ExternalChange;

    /// <inheritdoc />
    /// <remarks>
    /// A no-op for <see cref="BrowserStorageKindBlex.Session"/>: <c>sessionStorage</c> is per-tab,
    /// so there is no other instance to hear from.
    /// </remarks>
    public async ValueTask StartWatchingAsync(CancellationToken cancellationToken = default)
    {
        if (_watching || _area != "local")
            return;

        var module = await ModuleAsync(cancellationToken);
        _self ??= DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("watch", cancellationToken, _area, _self);
        _watching = true;
    }

    /// <summary>Invoked from JS when another tab wrote to this origin's storage.</summary>
    /// <param name="key">The changed key, or <c>null</c> when the area was cleared wholesale.</param>
    [JSInvokable]
    public Task OnExternalChangeAsync(string? key)
    {
        ExternalChange?.Invoke(key);
        return Task.CompletedTask;
    }

    private async ValueTask<IJSObjectReference> ModuleAsync(CancellationToken ct)
        => _module ??= await _js.InvokeAsync<IJSObjectReference>(
            "import", ct, "./_content/Blex.Blazor/blex-storage.js");

    /// <inheritdoc />
    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var module = await ModuleAsync(cancellationToken);
        return await module.InvokeAsync<string?>("get", cancellationToken, _area, key);
    }

    /// <inheritdoc />
    public async ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var module = await ModuleAsync(cancellationToken);
        await module.InvokeVoidAsync("set", cancellationToken, _area, key, value);
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var module = await ModuleAsync(cancellationToken);
        await module.InvokeVoidAsync("remove", cancellationToken, _area, key);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                // Detach the listener before dropping the module: it holds the DotNetObjectReference
                // below, and a live listener calling into a disposed reference throws on every
                // storage write in every other tab.
                if (_watching)
                {
                    await _module.InvokeVoidAsync("unwatch");
                    _watching = false;
                }

                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit gone; nothing to clean up.
        }
        finally
        {
            _self?.Dispose();
            _self = null;
        }
    }
}
