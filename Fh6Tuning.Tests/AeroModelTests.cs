using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// C#-native validation of the balanced-magnitude aero model (replaces JS parity for aero).
/// Encodes the model's INTENT: front and rear target equal downforce at 47% front weight; rear rises
/// +1.867 lbf per 1% front-weight above 47%; rear-engine never below balanced; front stays high on
/// Circuit; aero balance never pathological for representative kits; the handling-bias aero lever
/// shifts BALANCE kit-independently.
/// </summary>
public sealed class AeroModelTests
{
    private static readonly ITuningEngine Engine = new TuningEngine();

    // Representative kits: rear range ≥ front range (the realistic case). STD ≈ standard Forza kit
    // (front_max ≈ rear_min), BIG ≈ a WTAC-style big rear wing.
    private static readonly AeroRange StdF = new(30, 165), StdR = new(50, 300);
    private static readonly AeroRange BigF = new(235, 704), BigR = new(240, 1038);

    private static TuneInput Car(Drivetrain dt, EngineLocation el, double fwp, double power,
        AeroRange fr, AeroRange rr) => Fixtures.BaseInput(b =>
    {
        b.Drivetrain = dt; b.EngineLocation = el; b.FrontWeightPct = fwp; b.Power = power;
        b.PiClass = PiClass.S2; b.Weight = 3000; b.TireCompound = TireCompound.Race;
        b.SuspensionType = SuspensionType.Race;
        b.HasFrontAero = true; b.HasRearAero = true; b.AeroInstalled = true;
        b.AeroFront = fr; b.AeroRear = rr;
    });

    private static double Share(Tune t)
    {
        Assert.True(t.Aero.FrontLbf is not null && t.Aero.RearLbf is not null,
            "Share() requires a car with aero ranges (non-null lbf)");
        return t.Aero.FrontLbf!.Value / (t.Aero.FrontLbf!.Value + t.Aero.RearLbf!.Value);
    }

    [Fact] // Regression: the exact failing case. Was 100/10 (share 0.69).
    public void UserCar_Circuit_IsBalanced_FrontStaysHigh()
    {
        var car = Car(Drivetrain.AWD, EngineLocation.Mid, 47, 630, BigF, BigR);
        Tune t = Engine.Compute(car, Goal.Circuit);
        Assert.InRange(Share(t), 0.45, 0.55);              // balanced, not 0.69
        Assert.True(t.Aero.Front!.Value >= 85, $"front collapsed to {t.Aero.Front}");
    }

    [Fact] // At 47% front, front and rear target equal downforce.
    public void BalancedAt47_FrontApproxRearLbf()
    {
        foreach (var dt in new[] { Drivetrain.FWD, Drivetrain.RWD, Drivetrain.AWD })
        {
            Tune t = Engine.Compute(Car(dt, EngineLocation.Front, 47, 450, BigF, BigR), Goal.Circuit);
            Assert.True(Math.Abs(t.Aero.FrontLbf!.Value - t.Aero.RearLbf!.Value) <= 2,
                $"{dt}: front {t.Aero.FrontLbf} vs rear {t.Aero.RearLbf} not balanced at 47%");
        }
    }

    [Fact] // Front-heavy → more rear downforce (the verified 1.867 lb/% direction).
    public void RearDownforce_RisesWithFrontWeight()
    {
        Tune light = Engine.Compute(Car(Drivetrain.AWD, EngineLocation.Front, 47, 450, BigF, BigR), Goal.Circuit);
        Tune heavy = Engine.Compute(Car(Drivetrain.AWD, EngineLocation.Front, 57, 450, BigF, BigR), Goal.Circuit);
        Assert.True(heavy.Aero.RearLbf!.Value > light.Aero.RearLbf!.Value,
            $"57%-front rear {heavy.Aero.RearLbf} not > 47%-front rear {light.Aero.RearLbf}");
    }

    [Fact] // Rear-engine safeguard: never below balanced (rear ≥ front).
    public void RearEngine_NeverBelowBalanced()
    {
        Tune t = Engine.Compute(Car(Drivetrain.RWD, EngineLocation.Rear, 38, 450, BigF, BigR), Goal.Circuit);
        // −1 lbf tolerance: JsMath.Round can differ by 1 even when pre-round front == rear.
        Assert.True(t.Aero.RearLbf!.Value >= t.Aero.FrontLbf!.Value - 1,
            $"rear-engine front-biased: front {t.Aero.FrontLbf} > rear {t.Aero.RearLbf}");
        Assert.InRange(Share(t), 0.40, 0.51);
    }

    [Fact] // No pathological aero split anywhere on representative kits.
    public void AllFullKitCases_ShareInBand()
    {
        var dts = new[] { Drivetrain.FWD, Drivetrain.RWD, Drivetrain.AWD };
        var els = new[] { EngineLocation.Front, EngineLocation.Mid, EngineLocation.Rear };
        var fwps = new double[] { 42, 50, 57 };
        var kits = new[] { (StdF, StdR), (BigF, BigR) };
        var goals = new[] { Goal.Circuit, Goal.Touge, Goal.Rally, Goal.OffRoad, Goal.Drift };
        foreach (var dt in dts)
            foreach (var el in els)
                foreach (var fwp in fwps)
                    foreach (var (fr, rr) in kits)
                        foreach (var g in goals)
                        {
                            Tune t = Engine.Compute(Car(dt, el, fwp, 450, fr, rr), g);
                            double sh = Share(t);
                            Assert.True(sh >= 0.33 && sh <= 0.67,
                                $"{dt}/{el} {fwp}% {g}: aero share {sh:0.00} outside 0.33..0.67");
                        }
    }

    [Fact] // Circuit keeps front downforce high (does not collapse).
    public void Circuit_FrontStaysHigh()
    {
        foreach (var dt in new[] { Drivetrain.FWD, Drivetrain.RWD, Drivetrain.AWD })
        {
            Tune t = Engine.Compute(Car(dt, EngineLocation.Front, 50, 450, BigF, BigR), Goal.Circuit);
            Assert.True(t.Aero.Front!.Value >= 80, $"{dt}: Circuit front {t.Aero.Front} too low");
        }
    }

    [Fact] // The dial shifts aero balance by a fixed amount in SHARE space, independent of kit ranges.
    public void HandlingBiasAero_IsKitIndependent()
    {
        // -5 lowers front-share, moving both ends away from their range maxima so neither clamps.
        // The balance shift should be ≈ -0.08 (0.08 × BiasScale(5,1.05)=1) on BOTH the small STD kit
        // and the big BIG kit, proving the dial is kit-independent. The old ±8%-of-each-slider code
        // produced kit-dependent shifts (≈ -0.04 to -0.05) and would fail this assertion.
        foreach (var (fr, rr, label) in new[] { (StdF, StdR, "STD"), (BigF, BigR, "BIG") })
        {
            var car = Car(Drivetrain.AWD, EngineLocation.Front, 50, 450, fr, rr);
            double shift = Share(Engine.Compute(car with { HandlingBias = -5 }, Goal.Circuit))
                         - Share(Engine.Compute(car, Goal.Circuit));
            Assert.True(Math.Abs(shift - (-0.08)) < 0.01, $"{label}: balance shift {shift:0.000} not ≈ -0.08 (kit-dependent?)");
        }
    }

    [Fact] // +bias raises front-share (toward oversteer); -bias lowers it.
    public void HandlingBiasAero_ShiftsBalanceDirectionally()
    {
        var car = Car(Drivetrain.AWD, EngineLocation.Front, 50, 450, BigF, BigR);
        double bn = Share(Engine.Compute(car, Goal.Circuit));
        double bp = Share(Engine.Compute(car with { HandlingBias = 5 }, Goal.Circuit));
        double bm = Share(Engine.Compute(car with { HandlingBias = -5 }, Goal.Circuit));
        // BigF/BigR keep the shifted values clear of the range clamps, so strict monotonicity holds.
        Assert.True(bp > bn && bn > bm, $"not monotone: -5 {bm:0.00}, 0 {bn:0.00}, +5 {bp:0.00}");
    }

    [Fact] // No-ranges (fraction-space) full-kit path: balanced at 47%, weight-trim direction, rear-engine safeguard, lbf null.
    public void NoRanges_FractionSpace_BalancedAndSafeguarded()
    {
        // At 47% front the fraction-space front and rear are equal (balanced); lbf are null (no ranges).
        Tune at47 = Engine.Compute(Car(Drivetrain.AWD, EngineLocation.Front, 47, 450, AeroRange.None, AeroRange.None), Goal.Circuit);
        Assert.Null(at47.Aero.FrontLbf);
        Assert.Null(at47.Aero.RearLbf);
        Assert.Equal(at47.Aero.Front!.Value, at47.Aero.Rear!.Value);

        // Front-heavy → more rear (the 1.867/250 fraction-space trim).
        Tune heavy = Engine.Compute(Car(Drivetrain.AWD, EngineLocation.Front, 57, 450, AeroRange.None, AeroRange.None), Goal.Circuit);
        Assert.True(heavy.Aero.Rear!.Value > heavy.Aero.Front!.Value, $"front-heavy fraction-space rear {heavy.Aero.Rear} not > front {heavy.Aero.Front}");

        // Rear-engine safeguard holds in fraction space: rear ≥ front even for a rear-heavy car.
        Tune rearEng = Engine.Compute(Car(Drivetrain.RWD, EngineLocation.Rear, 40, 450, AeroRange.None, AeroRange.None), Goal.Circuit);
        Assert.True(rearEng.Aero.Rear!.Value >= rearEng.Aero.Front!.Value, $"rear-engine fraction-space rear {rearEng.Aero.Rear} < front {rearEng.Aero.Front}");
    }

    [Fact] // High-power, no-ranges: front clamps but rear keeps its weight trim (anchored to clamped front).
    public void NoRanges_HighPower_RearBiased_RearEasesBelowFront()
    {
        // 1320 hp → +25% front boost pushes level(0.85)×1.25 = 1.0625 past the 100% clamp.
        // The old code boosted rear independently, so rear also pinned at 100% and the −7% weight
        // trim vanished. Anchoring rear to the CLAMPED front keeps the trim: rear sits below front.
        Tune t = Engine.Compute(Car(Drivetrain.RWD, EngineLocation.Front, 40, 1320, AeroRange.None, AeroRange.None), Goal.Circuit);
        Assert.Equal(100, t.Aero.Front!.Value);                       // front clamps at full
        Assert.True(t.Aero.Rear!.Value < t.Aero.Front!.Value,
            $"rear-light car at high power: rear {t.Aero.Rear} not eased below front {t.Aero.Front}");
    }

    [Fact] // Drift runs a low, balanced split — not the old AWD rear-bias, not pathological.
    public void Drift_LowAndBalanced_NotRearBiased()
    {
        var car = Car(Drivetrain.AWD, EngineLocation.Front, 47, 450, BigF, BigR);
        Tune drift = Engine.Compute(car, Goal.Drift);
        Assert.InRange(Share(drift), 0.42, 0.58);                    // balanced at 47%, not rear-biased
        // Drift level (0.30) is below Circuit (0.85), so Drift front downforce sits lower.
        Tune circuit = Engine.Compute(car, Goal.Circuit);
        Assert.True(drift.Aero.FrontLbf!.Value < circuit.Aero.FrontLbf!.Value,
            $"Drift front DF {drift.Aero.FrontLbf} not below Circuit {circuit.Aero.FrontLbf}");
    }

    [Fact] // The full-kit why strings describe the weight-anchored model, not the retired drivetrain rule.
    public void FullKit_WhyStrings_DescribeWeightModel_NotDrivetrain()
    {
        Tune t = Engine.Compute(Car(Drivetrain.AWD, EngineLocation.Front, 50, 450, BigF, BigR), Goal.Circuit);
        Assert.DoesNotContain("drivetrain", t.Aero.Why.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AWD", t.Aero.Why.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("47", t.Aero.Why.Formula);                   // the balance anchor is stated
    }
}
