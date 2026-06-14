namespace Fh6Tuning.Core;

/// <summary>
/// Result of <c>validate()</c>: a guard run before rendering a tune. Mirrors the legacy
/// <c>{ valid, errors }</c> shape (legacy/tuning.js:897). The error strings and the validation
/// body are produced by the engine in a later phase; this is the immutable carrier record.
/// </summary>
public sealed record ValidationResult(bool Valid, IReadOnlyList<string> Errors);

/// <summary>
/// Per-goal UI metadata (label / icon / blurb), mirroring legacy <c>GOAL_META</c>
/// (legacy/tuning.js:29-36).
/// </summary>
public sealed record GoalMeta(string Label, string Icon, string Blurb);
