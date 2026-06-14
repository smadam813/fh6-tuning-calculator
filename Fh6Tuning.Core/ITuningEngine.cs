namespace Fh6Tuning.Core;

/// <summary>
/// The tuning engine surface. Mirrors the legacy module API
/// (<c>{ GOALS, GOAL_META, compute, validate, overallTireDiameter }</c>, legacy/tuning.js:947).
/// Implementations are pure and deterministic; the DI registration is a singleton.
/// </summary>
public interface ITuningEngine
{
    /// <summary>
    /// Pure, deterministic <c>compute(input, goal)</c> → full tune. At
    /// <see cref="TuneInput.HandlingBias"/>==0 AND <see cref="TuneInput.OverallStiffness"/>==0 the
    /// post-processors are SKIPPED, returning the per-goal baseline byte-for-byte.
    /// </summary>
    Tune Compute(TuneInput input, Goal goal);

    /// <summary>validate(raw) → guard before rendering. Mirrors legacy <c>validate()</c>.</summary>
    ValidationResult Validate(RawInput input);

    /// <summary>legacy <c>overallTireDiameter</c> — convenience forward to
    /// <see cref="TuneInput.OverallTireDiameter"/>.</summary>
    double? OverallTireDiameter(double? widthMm, double? aspectPct, double? rimIn);

    /// <summary>GOALS order (legacy/tuning.js:28).</summary>
    IReadOnlyList<Goal> Goals { get; }

    /// <summary>GOAL_META (legacy/tuning.js:29-36).</summary>
    IReadOnlyDictionary<Goal, GoalMeta> GoalMeta { get; }
}
