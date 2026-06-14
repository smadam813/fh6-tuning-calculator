using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// Shared assertion helpers for the engine suite — the C# port of <c>legacy/test/helpers.js</c>:
/// <see cref="InRange"/>, <see cref="AssertAllInRange"/>, <see cref="AssertSpringInPart"/>,
/// <see cref="AssertWhyShape"/>. Centralizes range-checking so any future formula drift that escapes
/// a clamp is caught.
/// </summary>
public static class Helpers
{
    /// <summary>Assert a numeric value is finite and within [lo, hi] inclusive.</summary>
    public static void InRange(double value, (double lo, double hi) range, string label)
    {
        Assert.True(!double.IsNaN(value) && !double.IsInfinity(value),
            $"{label}: expected a finite number, got {value}");
        Assert.True(value >= range.lo && value <= range.hi,
            $"{label}: {value} is outside legal range [{range.lo}, {range.hi}]");
    }

    /// <summary>Verify EVERY numeric output of a tune lands inside its legal slider range.</summary>
    public static void AssertAllInRange(Tune t)
    {
        InRange(t.Tires.Front, Fixtures.Ranges.TirePsi, "tires.front");
        InRange(t.Tires.Rear, Fixtures.Ranges.TirePsi, "tires.rear");

        InRange(t.Gearing.Final, Fixtures.Ranges.Fd, "gearing.final");
        for (int idx = 0; idx < t.Gearing.Ratios.Count; idx++)
            InRange(t.Gearing.Ratios[idx], Fixtures.Ranges.Gear, $"gearing.ratios[{idx}]");

        InRange(t.Alignment.CamberF, Fixtures.Ranges.Camber, "alignment.camberF");
        InRange(t.Alignment.CamberR, Fixtures.Ranges.Camber, "alignment.camberR");
        InRange(t.Alignment.ToeF, Fixtures.Ranges.Toe, "alignment.toeF");
        InRange(t.Alignment.ToeR, Fixtures.Ranges.Toe, "alignment.toeR");
        InRange(t.Alignment.Caster, Fixtures.Ranges.Caster, "alignment.caster");

        InRange(t.Arb.Front, Fixtures.Ranges.Arb, "arb.front");
        InRange(t.Arb.Rear, Fixtures.Ranges.Arb, "arb.rear");

        // spring rate is clamped to the part's own min/max (checked by AssertSpringInPart)
        InRange(t.Damping.ReboundF, Fixtures.Ranges.Damping, "damping.reboundF");
        InRange(t.Damping.ReboundR, Fixtures.Ranges.Damping, "damping.reboundR");
        InRange(t.Damping.BumpF, Fixtures.Ranges.Damping, "damping.bumpF");
        InRange(t.Damping.BumpR, Fixtures.Ranges.Damping, "damping.bumpR");

        if (t.Aero.Applicable)
        {
            if (t.Aero.Front is double af) InRange(af, Fixtures.Ranges.AeroPct, "aero.front");
            if (t.Aero.Rear is double ar) InRange(ar, Fixtures.Ranges.AeroPct, "aero.rear");
        }

        InRange(t.Braking.Balance, Fixtures.Ranges.BrakeBalance, "braking.balance");
        InRange(t.Braking.Pressure, Fixtures.Ranges.BrakePressure, "braking.pressure");

        InRange(t.Differential.Accel, Fixtures.Ranges.Diff, "differential.accel");
        InRange(t.Differential.Decel, Fixtures.Ranges.Diff, "differential.decel");
        if (t.Differential.Driveline == Drivetrain.AWD)
        {
            InRange(t.Differential.FrontAccel!.Value, Fixtures.Ranges.Diff, "differential.frontAccel");
            InRange(t.Differential.FrontDecel!.Value, Fixtures.Ranges.Diff, "differential.frontDecel");
            InRange(t.Differential.CenterRear!.Value, Fixtures.Ranges.AwdCenter, "differential.centerRear");
        }
    }

    /// <summary>Assert spring rate &amp; ride height within the supplied part min/max.</summary>
    public static void AssertSpringInPart(Tune t, TuneInput input)
    {
        InRange(t.Springs.Front, (input.SpringRateMinF!.Value, input.SpringRateMaxF!.Value), "springs.front");
        InRange(t.Springs.Rear, (input.SpringRateMinR!.Value, input.SpringRateMaxR!.Value), "springs.rear");
        InRange(t.Springs.RideF, (input.RideHeightMinF!.Value, input.RideHeightMaxF!.Value), "springs.rideF");
        InRange(t.Springs.RideR, (input.RideHeightMinR!.Value, input.RideHeightMaxR!.Value), "springs.rideR");
    }

    /// <summary>Every <c>why</c> field must be {text, formula} non-empty strings.</summary>
    public static void AssertWhyShape(Tune t)
    {
        var whys = new (string name, Why why)[]
        {
            ("tires", t.Tires.Why),
            ("gearing", t.Gearing.Why),
            ("alignment", t.Alignment.Why),
            ("arb", t.Arb.Why),
            ("springs", t.Springs.Why),
            ("damping", t.Damping.Why),
            ("aero", t.Aero.Why),
            ("braking", t.Braking.Why),
            ("differential", t.Differential.Why),
        };
        foreach (var (name, why) in whys)
        {
            Assert.True(why is not null && why.Text.Length > 0, $"{name}.why.text must be a non-empty string");
            Assert.True(why!.Formula.Length > 0, $"{name}.why.formula must be a non-empty string");
        }
    }
}
