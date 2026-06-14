using System.Text.Json.Serialization;

namespace Fh6Tuning.Core;

/// <summary>
/// The engine input record (imperial only). Immutable; holds the complete field set the engine
/// reads, taken from <c>readInputs()</c> (legacy/app.js:84-123) + <c>derive()</c> + the per-axle
/// fallbacks in <c>springs()</c>/<c>validate()</c>. All units imperial (lb, in, lb/in, hp, lb-ft,
/// mph, rpm); the Web layer converts before constructing this.
/// </summary>
public sealed record TuneInput
{
    // ---- categorical ----
    public required Drivetrain Drivetrain { get; init; }
    public required EngineLocation EngineLocation { get; init; }
    public required Powertrain Powertrain { get; init; }
    public required PiClass PiClass { get; init; }
    public required TireCompound TireCompound { get; init; }
    public required SuspensionType SuspensionType { get; init; }

    // ---- core numerics (required) ----
    public required double Power { get; init; }          // hp
    public required double Torque { get; init; }         // lb-ft
    public required double Weight { get; init; }         // lb
    public required double FrontWeightPct { get; init; } // 0..100
    public required double Gears { get; init; }          // engine does Math.round then clamp[2,10]

    // ---- aero kit booleans (engine reads these, not the kit) ----
    public required bool HasFrontAero { get; init; }
    public required bool HasRearAero { get; init; }
    public required bool AeroInstalled { get; init; }

    // ---- per-axle spring rate part range (lb/in) ----
    public double? SpringRateMinF { get; init; }
    public double? SpringRateMaxF { get; init; }
    public double? SpringRateMinR { get; init; }
    public double? SpringRateMaxR { get; init; }

    // ---- per-axle ride height part range (in) ----
    public double? RideHeightMinF { get; init; }
    public double? RideHeightMaxF { get; init; }
    public double? RideHeightMinR { get; init; }
    public double? RideHeightMaxR { get; init; }

    // ---- legacy shared range fallbacks ----
    public double? SpringRateMin { get; init; }
    public double? SpringRateMax { get; init; }
    public double? RideHeightMin { get; init; }
    public double? RideHeightMax { get; init; }

    // ---- optional downforce ranges (imperial lbf) ----
    public AeroRange AeroFront { get; init; } = AeroRange.None;
    public AeroRange AeroRear { get; init; } = AeroRange.None;

    // ---- optional gearing physics (null OR <=0 → HP heuristic) ----
    public double? RedlineRpm { get; init; }
    public double? TireDiameter { get; init; }   // rolling Ø, in (from OverallTireDiameter)
    public double? TargetTopSpeed { get; init; } // mph

    // ---- post-process dials (0 = pure baseline) ----
    public double HandlingBias { get; init; }     // -5 understeer … +5 oversteer
    public double OverallStiffness { get; init; } // -5 soft … +5 hard

    // ===== resolved accessors (engine reads these; mirror legacy != null ? : picks) =====
    [JsonIgnore] public double SpringMinF => SpringRateMinF ?? SpringRateMin ?? double.NaN;
    [JsonIgnore] public double SpringMaxF => SpringRateMaxF ?? SpringRateMax ?? double.NaN;
    [JsonIgnore] public double SpringMinR => SpringRateMinR ?? SpringRateMin ?? double.NaN;
    [JsonIgnore] public double SpringMaxR => SpringRateMaxR ?? SpringRateMax ?? double.NaN;
    [JsonIgnore] public double RideMinF => RideHeightMinF ?? RideHeightMin ?? double.NaN;
    [JsonIgnore] public double RideMaxF => RideHeightMaxF ?? RideHeightMax ?? double.NaN;
    [JsonIgnore] public double RideMinR => RideHeightMinR ?? RideHeightMin ?? double.NaN;
    [JsonIgnore] public double RideMaxR => RideHeightMaxR ?? RideHeightMax ?? double.NaN;

    [JsonIgnore] public bool CanComputeSpeeds => RedlineRpm is > 0 && TireDiameter is > 0;
    [JsonIgnore] public bool HasTargetTopSpeed => TargetTopSpeed is > 0;

    /// <summary>
    /// legacy <c>overallTireDiameter()</c>: <c>rim + 2×(width×aspect/100)/25.4</c>, in inches,
    /// or null on any non-positive/blank part. width mm, aspect %, rim in.
    /// </summary>
    public static double? OverallTireDiameter(double? widthMm, double? aspectPct, double? rimIn)
    {
        if (widthMm is not > 0 || aspectPct is not > 0 || rimIn is not > 0) return null;
        return rimIn.Value + 2 * (widthMm.Value * (aspectPct.Value / 100)) / 25.4;
    }
}
