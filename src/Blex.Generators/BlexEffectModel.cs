namespace Blex.Generators;

/// <param name="Concurrency">0 = Parallel, 1 = Latest, 2 = Drop, 3 = Queue (mirrors Blex.BlexEffectConcurrency).</param>
/// <param name="HasCancellationToken">True when the impl method's last parameter is a CancellationToken supplied by the wrapper.</param>
/// <param name="DebounceMilliseconds">Quiet period before the body runs; 0 disables debouncing.</param>
internal sealed record BlexEffectModel(
    string ImplName,
    string WrapperName,
    string ActionLabel,
    bool IsValueTask,
    int Concurrency,
    bool HasCancellationToken,
    int DebounceMilliseconds,
    BlexEquatableArray<BlexParamModel> Parameters) : System.IEquatable<BlexEffectModel>;
