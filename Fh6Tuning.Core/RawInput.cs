namespace Fh6Tuning.Core;

/// <summary>
/// Raw, unparsed engine input — exactly the key set legacy <c>validate()</c> reads, PLUS the shared
/// range keys <c>failure.test.js</c> exercises. Every field is a <see cref="RawValue"/> so
/// blank / non-numeric / NaN / absent are all representable. The Web layer fills these from the
/// form; the parity / failure tests fill them directly.
/// </summary>
public sealed record RawInput
{
    public RawValue Power { get; init; } = RawValue.Absent;
    public RawValue Weight { get; init; } = RawValue.Absent;
    public RawValue FrontWeightPct { get; init; } = RawValue.Absent;
    public RawValue Torque { get; init; } = RawValue.Absent;
    public RawValue Gears { get; init; } = RawValue.Absent;

    // per-axle ranges
    public RawValue SpringRateMinF { get; init; } = RawValue.Absent;
    public RawValue SpringRateMaxF { get; init; } = RawValue.Absent;
    public RawValue SpringRateMinR { get; init; } = RawValue.Absent;
    public RawValue SpringRateMaxR { get; init; } = RawValue.Absent;
    public RawValue RideHeightMinF { get; init; } = RawValue.Absent;
    public RawValue RideHeightMaxF { get; init; } = RawValue.Absent;
    public RawValue RideHeightMinR { get; init; } = RawValue.Absent;
    public RawValue RideHeightMaxR { get; init; } = RawValue.Absent;

    // shared fallbacks (failure.test.js uses these)
    public RawValue SpringRateMin { get; init; } = RawValue.Absent;
    public RawValue SpringRateMax { get; init; } = RawValue.Absent;
    public RawValue RideHeightMin { get; init; } = RawValue.Absent;
    public RawValue RideHeightMax { get; init; } = RawValue.Absent;
}
