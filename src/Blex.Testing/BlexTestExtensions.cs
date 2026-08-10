using System.Text.Json.Nodes;

namespace Blex.Testing;

/// <summary>Convenience extensions for testing Blex stores and managers.</summary>
public static class BlexTestExtensions
{
    /// <summary>Starts recording dispatched actions on the manager. Dispose the result to stop.</summary>
    public static BlexActionLog RecordActions(this BlexManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return new BlexActionLog(manager);
    }

    /// <summary>Returns the store's current state as a JSON snapshot (alias for SerializeState).</summary>
    public static JsonObject Snapshot(this IBlexStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.SerializeState();
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> becomes true, re-evaluating it on every store
    /// change. Throws <see cref="TimeoutException"/> after <paramref name="timeout"/> (default
    /// 5 seconds). The async analog of "waitFor" helpers in JS testing libraries -- useful for
    /// asserting on effects without arbitrary <c>Task.Delay</c> calls.
    /// </summary>
    /// <example><code>await store.WaitForAsync(() => !store.LoadIsLoading);</code></example>
    public static async Task WaitForAsync(this IBlexStore store, Func<bool> condition, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(condition);

        if (condition())
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler()
        {
            try
            {
                if (condition())
                    tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        store.StateChanged += Handler;
        using var timeoutCts = new CancellationTokenSource();
        try
        {
            // Re-check after subscribing to close the race with a change that happened in between.
            Handler();
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout ?? TimeSpan.FromSeconds(5), timeoutCts.Token)).ConfigureAwait(false);
            if (completed != tcs.Task)
                throw new TimeoutException("The store did not reach the expected state in time.");
            timeoutCts.Cancel(); // release the timer instead of letting it run out
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            store.StateChanged -= Handler;
        }
    }

    /// <summary>
    /// Counts how many times the store notifies <see cref="IBlexStore.StateChanged"/> while running
    /// <paramref name="act"/>. Useful for asserting batching (e.g. one notification per action).
    /// </summary>
    public static int CountNotifications(this IBlexStore store, Action act)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(act);
        var count = 0;
        void Handler() => count++;
        store.StateChanged += Handler;
        try
        {
            act();
        }
        finally
        {
            store.StateChanged -= Handler;
        }

        return count;
    }
}
