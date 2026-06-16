using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// Shared test fixtures — the C# port of <c>legacy/test/fixtures.js</c>. A complete imperial base
/// input with every field present, the three canonical cars (each overriding only what differs),
/// and the legal slider <see cref="Ranges"/> used by the "within range" assertions.
/// </summary>
public static class Fixtures
{
    /// <summary>The shared <see cref="ITuningEngine"/> handle (legacy <c>TUNING</c>).</summary>
    public static readonly ITuningEngine TUNING = new TuningEngine();

    /// <summary>
    /// A complete imperial input with every field present (legacy <c>baseInput()</c>). The
    /// <paramref name="overrides"/> mutator overrides only what differs, so each car is a single
    /// coherent object. RWD / front-engine / ICE / A-class / full aero / Race suspension, dials 0.
    /// </summary>
    public static TuneInput BaseInput(Action<TuneInputBuilder>? overrides = null)
    {
        var b = new TuneInputBuilder
        {
            Drivetrain = Drivetrain.RWD,
            EngineLocation = EngineLocation.Front,
            Powertrain = Powertrain.ICE,
            PiClass = PiClass.A,
            Power = 400,
            Torque = 370,
            Weight = 3300,
            FrontWeightPct = 52,
            Gears = 6,
            TireCompound = TireCompound.Sport,
            SuspensionType = SuspensionType.Race,
            HasFrontAero = true,
            HasRearAero = true,
            AeroInstalled = true,
            RideHeightMinF = 4.5,
            RideHeightMaxF = 7.0,
            RideHeightMinR = 4.5,
            RideHeightMaxR = 7.0,
            SpringRateMinF = 150,
            SpringRateMaxF = 900,
            SpringRateMinR = 150,
            SpringRateMaxR = 900,
            AeroFront = AeroRange.None,
            AeroRear = AeroRange.None,
            RedlineRpm = null,
            TireDiameter = null,
            TargetTopSpeed = null,
            HandlingBias = 0,
            OverallStiffness = 0,
        };
        overrides?.Invoke(b);
        return b.Build();
    }

    // 1) Lightweight RWD ICE — front-engine, no aero, mid power. Hot hatch / coupe.
    public static TuneInput CarLightRwd => BaseInput(b =>
    {
        b.Drivetrain = Drivetrain.RWD;
        b.EngineLocation = EngineLocation.Front;
        b.Powertrain = Powertrain.ICE;
        b.PiClass = PiClass.B;
        b.Power = 300;
        b.Torque = 280;
        b.Weight = 2600;
        b.FrontWeightPct = 53;
        b.Gears = 6;
        b.TireCompound = TireCompound.Sport;
        b.SuspensionType = SuspensionType.Race;
        b.HasFrontAero = false;
        b.HasRearAero = false;
        b.AeroInstalled = false;
    });

    // 2) Heavy AWD EV — single-speed, nose-heavy, big mass, full aero kit.
    public static TuneInput CarHeavyAwdEv => BaseInput(b =>
    {
        b.Drivetrain = Drivetrain.AWD;
        b.EngineLocation = EngineLocation.Front;
        b.Powertrain = Powertrain.EV;
        b.PiClass = PiClass.S1;
        b.Power = 760;
        b.Torque = 720;
        b.Weight = 5200;
        b.FrontWeightPct = 54;
        b.Gears = 1;
        b.TireCompound = TireCompound.Race;
        b.SuspensionType = SuspensionType.Race;
        b.HasFrontAero = true;
        b.HasRearAero = true;
        b.AeroInstalled = true;
    });

    // 3) Mid-engine RWD high-PI — rear-biased, high power, full aero.
    public static TuneInput CarMidRwdHighPi => BaseInput(b =>
    {
        b.Drivetrain = Drivetrain.RWD;
        b.EngineLocation = EngineLocation.Mid;
        b.Powertrain = Powertrain.ICE;
        b.PiClass = PiClass.S2;
        b.Power = 720;
        b.Torque = 560;
        b.Weight = 3100;
        b.FrontWeightPct = 43;
        b.Gears = 7;
        b.TireCompound = TireCompound.Race;
        b.SuspensionType = SuspensionType.Race;
        b.HasFrontAero = true;
        b.HasRearAero = true;
        b.AeroInstalled = true;
    });

    /// <summary>Legal slider ranges (clamp targets) for "within range" assertions (legacy RANGES).</summary>
    public static class Ranges
    {
        public static readonly (double lo, double hi) TirePsi = (15, 55);
        public static readonly (double lo, double hi) Fd = (2, 7);
        public static readonly (double lo, double hi) Gear = (0.5, 5.5);
        public static readonly (double lo, double hi) Camber = (-5, 0);
        public static readonly (double lo, double hi) Toe = (-5, 5);
        public static readonly (double lo, double hi) Caster = (1, 7);
        public static readonly (double lo, double hi) Arb = (1, 65);
        public static readonly (double lo, double hi) Damping = (1, 20);
        public static readonly (double lo, double hi) AeroPct = (0, 100);
        public static readonly (double lo, double hi) BrakeBalance = (40, 65);
        public static readonly (double lo, double hi) BrakePressure = (80, 130);
        public static readonly (double lo, double hi) Diff = (0, 100);
        public static readonly (double lo, double hi) AwdCenter = (50, 90);
    }
}

/// <summary>
/// A mutable builder mirroring the JS <c>Object.assign(base, overrides)</c> ergonomics so each test's
/// override block reads like the JS fixtures. Immutable <see cref="TuneInput"/> is produced by
/// <see cref="Build"/>.
/// </summary>
public sealed class TuneInputBuilder
{
    public Drivetrain Drivetrain { get; set; }
    public EngineLocation EngineLocation { get; set; }
    public Powertrain Powertrain { get; set; }
    public PiClass PiClass { get; set; }
    public TireCompound TireCompound { get; set; }
    public SuspensionType SuspensionType { get; set; }
    public double Power { get; set; }
    public double Torque { get; set; }
    public double Weight { get; set; }
    public double FrontWeightPct { get; set; }
    public double Gears { get; set; }
    public bool HasFrontAero { get; set; }
    public bool HasRearAero { get; set; }
    public bool AeroInstalled { get; set; }
    public double? SpringRateMinF { get; set; }
    public double? SpringRateMaxF { get; set; }
    public double? SpringRateMinR { get; set; }
    public double? SpringRateMaxR { get; set; }
    public double? RideHeightMinF { get; set; }
    public double? RideHeightMaxF { get; set; }
    public double? RideHeightMinR { get; set; }
    public double? RideHeightMaxR { get; set; }
    public double? SpringRateMin { get; set; }
    public double? SpringRateMax { get; set; }
    public double? RideHeightMin { get; set; }
    public double? RideHeightMax { get; set; }
    public AeroRange AeroFront { get; set; } = AeroRange.None;
    public AeroRange AeroRear { get; set; } = AeroRange.None;
    public double? RedlineRpm { get; set; }
    public double? TireDiameter { get; set; }
    public double? TargetTopSpeed { get; set; }
    public double? PeakPowerRpm { get; set; }
    public double? MaxTorqueRpm { get; set; }
    public double HandlingBias { get; set; }
    public double OverallStiffness { get; set; }

    public TuneInput Build() => new()
    {
        Drivetrain = Drivetrain,
        EngineLocation = EngineLocation,
        Powertrain = Powertrain,
        PiClass = PiClass,
        TireCompound = TireCompound,
        SuspensionType = SuspensionType,
        Power = Power,
        Torque = Torque,
        Weight = Weight,
        FrontWeightPct = FrontWeightPct,
        Gears = Gears,
        HasFrontAero = HasFrontAero,
        HasRearAero = HasRearAero,
        AeroInstalled = AeroInstalled,
        SpringRateMinF = SpringRateMinF,
        SpringRateMaxF = SpringRateMaxF,
        SpringRateMinR = SpringRateMinR,
        SpringRateMaxR = SpringRateMaxR,
        RideHeightMinF = RideHeightMinF,
        RideHeightMaxF = RideHeightMaxF,
        RideHeightMinR = RideHeightMinR,
        RideHeightMaxR = RideHeightMaxR,
        SpringRateMin = SpringRateMin,
        SpringRateMax = SpringRateMax,
        RideHeightMin = RideHeightMin,
        RideHeightMax = RideHeightMax,
        AeroFront = AeroFront,
        AeroRear = AeroRear,
        RedlineRpm = RedlineRpm,
        TireDiameter = TireDiameter,
        TargetTopSpeed = TargetTopSpeed,
        PeakPowerRpm = PeakPowerRpm,
        MaxTorqueRpm = MaxTorqueRpm,
        HandlingBias = HandlingBias,
        OverallStiffness = OverallStiffness,
    };
}
