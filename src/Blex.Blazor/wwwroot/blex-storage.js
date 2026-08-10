// Thin bridge over the browser's Web Storage areas used by Blex persistence.
// `area` is "local" or "session"; failures (private mode, quota) degrade to no-ops.

function store(area) {
    return area === "session" ? window.sessionStorage : window.localStorage;
}

export function get(area, key) {
    try {
        return store(area).getItem(key);
    } catch {
        return null;
    }
}

export function set(area, key, value) {
    // Deliberately not swallowed. A failed write (quota exhausted, private mode, storage disabled)
    // means the state the user thinks is saved is not saved; reporting it through Blex's OnError
    // lets the app say so, where a console warning nobody reads means silent data loss.
    store(area).setItem(key, value);
}

export function remove(area, key) {
    try {
        store(area).removeItem(key);
    } catch {
        // ignore
    }
}

// Cross-tab sync. The browser's `storage` event fires only in the *other* tabs sharing the origin,
// never in the one that wrote the value, so this cannot echo our own writes back at us.
// `e.key` is null when the area was cleared wholesale; that is forwarded as-is.
let watcher = null;

export function watch(area, handler) {
    if (watcher) {
        unwatch();
    }

    // sessionStorage is per-tab, so a storage event can never concern it; watching it would only
    // deliver another tab's localStorage writes under the wrong area.
    if (area !== "local") {
        return;
    }

    watcher = e => {
        if (e.storageArea && e.storageArea !== window.localStorage) {
            return;
        }

        // Fire and forget: an exception on the .NET side must not break the page's event loop.
        handler.invokeMethodAsync("OnExternalChangeAsync", e.key).catch(() => { });
    };

    window.addEventListener("storage", watcher);
}

export function unwatch() {
    if (watcher) {
        window.removeEventListener("storage", watcher);
        watcher = null;
    }
}
