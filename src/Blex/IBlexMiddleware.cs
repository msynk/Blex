using System.Text.Json.Nodes;

namespace Blex;

/// <summary>
/// A pipeline hook invoked for every dispatched action. Use for logging, analytics, persistence, etc.
/// Middleware runs synchronously and must not throw; exceptions are swallowed and reported to other middleware.
/// </summary>
public interface IBlexMiddleware
{
    /// <summary>Invoked after an action has been applied to its store.</summary>
    void OnAction(BlexActionContext context);

    /// <summary>
    /// Invoked before an action runs. Override to inspect or <see cref="BlexPreActionContext.Cancel">veto</see>
    /// the action. The default implementation does nothing (the action proceeds).
    /// </summary>
    void BeforeAction(BlexPreActionContext context)
    {
    }
}
