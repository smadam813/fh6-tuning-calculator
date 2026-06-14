using System.Text.Json.Serialization;

namespace Fh6Tuning.Core;

// Output record graph. Field names mirror the JS `tune` object leaf-for-leaf so serialized JSON
// matches JSON.stringify(compute(...)). Every numeric output field is double / double? —
// System.Text.Json writes integral doubles without a ".0" suffix, matching JS.

public sealed record Why(string Text, string Formula);

public sealed record SummaryChip(string K, string V);

public sealed record Tires(double Front, double Rear, Why Why);

public sealed record Gearing(
    double Final,
    IReadOnlyList<double> Ratios,
    bool SingleSpeed,
    IReadOnlyList<double>? Speeds, // null when !CanComputeSpeeds
    double? TopSpeed,             // null when no speeds
    string FdSource,             // "heuristic" | "target"
    Why Why);

public sealed record Alignment(
    double CamberF, double CamberR, double ToeF, double ToeR, double Caster, Why Why);

public sealed record Arb(double Front, double Rear, Why Why);

// _fFront/_fRear are JS internal scratch (used only by the why string); NOT on the record.
public sealed record Springs(
    double Front, double Rear, double RideF, double RideR, Why Why);

public sealed record Damping(
    double ReboundF, double ReboundR, double BumpF, double BumpR, Why Why);

// Applicable=false → Front/Rear null. Single wing → only its own end populated.
public sealed record Aero(
    bool Applicable,
    double? Front, double? FrontLbf,
    double? Rear, double? RearLbf,
    Why Why);

public sealed record Braking(double Balance, double Pressure, Why Why);

// FWD/RWD: FrontAccel/FrontDecel/CenterRear stay null. AWD populates all five.
// Driveline echoes the input drivetrain. It is a Drivetrain enum (not a string): the existing
// JsonStringEnumConverter<Drivetrain> serializes it to the same "FWD"/"RWD"/"AWD" tokens the JS
// engine emitted, so parity-JSON byte-identity is preserved while the engine + Web layer switch
// on the typed value instead of string literals.
public sealed record Differential(
    Drivetrain Driveline,
    double Accel, double Decel,
    double? FrontAccel, double? FrontDecel, double? CenterRear,
    Why Why);

public sealed record Derived(
    double Frac, double RearFrac,
    double FrontAxle, double RearAxle,
    double FrontCorner, double RearCorner,
    double Pw,
    int PiIdx,
    ClassTier ClassTier,
    double GripFactor,
    bool UndersteerProne, bool OversteerProne,
    bool CanTuneSusp,
    double EvFactor,
    [property: JsonPropertyName("isEV")] bool IsEv); // legacy key is isEV, not isEv

public sealed record Tune(
    Goal Goal,
    Derived Derived,
    IReadOnlyList<SummaryChip> Summary, // exactly 5 chips, fixed order
    Tires Tires,
    Gearing Gearing,
    Alignment Alignment,
    Arb Arb,
    Springs Springs,
    Damping Damping,
    Aero Aero,
    Braking Braking,
    Differential Differential);
