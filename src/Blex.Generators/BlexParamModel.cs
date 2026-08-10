namespace Blex.Generators;

/// <param name="DefaultLiteral">C# literal for the parameter's default value, or null when none.</param>
/// <param name="IsParams">Whether this is a <c>params</c> array parameter.</param>
internal sealed record BlexParamModel(string TypeFqn, string Name, string? DefaultLiteral = null, bool IsParams = false) : System.IEquatable<BlexParamModel>;
