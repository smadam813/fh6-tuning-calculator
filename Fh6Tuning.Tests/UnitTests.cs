using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// UNIT TESTS — one test per output parameter, across three distinct cars (the C# port of
/// <c>legacy/test/unit.test.js</c>). For each parameter we assert (a) the EXACT value the engine
/// produces for known inputs (locked-in via Circuit-goal computations) and (b) that the value sits
/// inside its legal slider min/max. Exact values are the regression contract.
///
/// <para>L = CarLightRwd, E = CarHeavyAwdEv, M = CarMidRwdHighPi.</para>
/// </summary>
public sealed class UnitTests
{
    private static readonly Tune L = Fixtures.TUNING.Compute(Fixtures.CarLightRwd, Goal.Circuit);
    private static readonly Tune E = Fixtures.TUNING.Compute(Fixtures.CarHeavyAwdEv, Goal.Circuit);
    private static readonly Tune M = Fixtures.TUNING.Compute(Fixtures.CarMidRwdHighPi, Goal.Circuit);

    private static readonly TuneInput LCar = Fixtures.CarLightRwd;
    private static readonly TuneInput ECar = Fixtures.CarHeavyAwdEv;
    private static readonly TuneInput MCar = Fixtures.CarMidRwdHighPi;

    /* ---------------- TIRES ---------------- */
    [Fact]
    public void TiresFront_ExactAndInRange()
    {
        Assert.Equal(29.5, L.Tires.Front);
        Assert.Equal(33.5, E.Tires.Front);
        Assert.Equal(32, M.Tires.Front);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Tires.Front, Fixtures.Ranges.TirePsi, "tires.front");
    }

    [Fact]
    public void TiresRear_ExactAndInRange()
    {
        Assert.Equal(29, L.Tires.Rear);
        Assert.Equal(33, E.Tires.Rear);
        Assert.Equal(31, M.Tires.Rear);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Tires.Rear, Fixtures.Ranges.TirePsi, "tires.rear");
    }

    /* ---------------- GEARING ---------------- */
    [Fact]
    public void GearingFinal_ExactAndInRange()
    {
        Assert.Equal(4.34, L.Gearing.Final);
        Assert.Equal(3.89, E.Gearing.Final);
        Assert.Equal(3.61, M.Gearing.Final);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Gearing.Final, Fixtures.Ranges.Fd, "gearing.final");
    }

    [Fact]
    public void GearingRatios_ExactInRangeStrictlyDescending()
    {
        Assert.Equal(new double[] { 3.21, 2.05, 1.57, 1.3, 1.13, 1 }, L.Gearing.Ratios);
        Assert.Equal(new double[] { 1.39 }, E.Gearing.Ratios); // single-speed EV
        Assert.Equal(new double[] { 2.88, 1.83, 1.41, 1.17, 1.01, 0.9, 0.81 }, M.Gearing.Ratios);
        foreach (var t in new[] { L, E, M })
        {
            for (int idx = 0; idx < t.Gearing.Ratios.Count; idx++)
                Helpers.InRange(t.Gearing.Ratios[idx], Fixtures.Ranges.Gear, $"ratios[{idx}]");
            for (int k = 1; k < t.Gearing.Ratios.Count; k++)
                Assert.True(t.Gearing.Ratios[k] < t.Gearing.Ratios[k - 1], "ratios must strictly descend");
        }
    }

    [Fact]
    public void GearingSingleSpeed_EvTrueOthersFalse_RatiosLengthMatchesGears()
    {
        Assert.False(L.Gearing.SingleSpeed);
        Assert.True(E.Gearing.SingleSpeed);
        Assert.False(M.Gearing.SingleSpeed);
        Assert.Equal((int)LCar.Gears, L.Gearing.Ratios.Count);
        Assert.Single(E.Gearing.Ratios);
        Assert.Equal((int)MCar.Gears, M.Gearing.Ratios.Count);
    }

    [Fact]
    public void OverallTireDiameter_Fh6Spec()
    {
        // rim + 2 × (width × aspect/100) / 25.4
        Assert.True(Math.Abs(Fixtures.TUNING.OverallTireDiameter(315, 30, 17)!.Value - 24.4409) < 1e-3);
        Assert.True(Math.Abs(Fixtures.TUNING.OverallTireDiameter(225, 45, 17)!.Value - 24.9724) < 1e-3);
        Assert.True(Math.Abs(Fixtures.TUNING.OverallTireDiameter(245, 40, 19)!.Value - 26.7165) < 1e-3);
        // any blank/non-positive part → null (caller falls back to the HP heuristic)
        Assert.Null(Fixtures.TUNING.OverallTireDiameter(null, 30, 17));
        Assert.Null(Fixtures.TUNING.OverallTireDiameter(315, 0, 17));
        Assert.Null(Fixtures.TUNING.OverallTireDiameter(315, 30, null)); // "" in JS coerces to NaN → null
    }

    /* ---------------- ALIGNMENT ---------------- */
    [Fact]
    public void AlignmentCamberF_ExactAndInRange()
    {
        Assert.Equal(-2.1, L.Alignment.CamberF);
        Assert.Equal(-2.3, E.Alignment.CamberF);
        Assert.Equal(-2.3, M.Alignment.CamberF);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Alignment.CamberF, Fixtures.Ranges.Camber, "camberF");
    }

    [Fact]
    public void AlignmentCamberR_ExactAndInRange()
    {
        Assert.Equal(-0.9, L.Alignment.CamberR);
        Assert.Equal(-1, E.Alignment.CamberR);
        Assert.Equal(-1.2, M.Alignment.CamberR);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Alignment.CamberR, Fixtures.Ranges.Camber, "camberR");
    }

    [Fact]
    public void AlignmentToeF_ExactAndInRange()
    {
        // engine NormZero already collapses -0 to 0
        Assert.Equal(0, L.Alignment.ToeF);
        Assert.Equal(0, E.Alignment.ToeF);
        Assert.Equal(0, M.Alignment.ToeF);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Alignment.ToeF, Fixtures.Ranges.Toe, "toeF");
    }

    [Fact]
    public void AlignmentToeR_ExactAndInRange()
    {
        Assert.Equal(0.1, L.Alignment.ToeR);
        Assert.Equal(0, E.Alignment.ToeR);
        Assert.Equal(0.2, M.Alignment.ToeR);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Alignment.ToeR, Fixtures.Ranges.Toe, "toeR");
    }

    [Fact]
    public void AlignmentCaster_ExactAndInRange()
    {
        Assert.Equal(5.2, L.Alignment.Caster);
        Assert.Equal(7, E.Alignment.Caster);
        Assert.Equal(6.4, M.Alignment.Caster);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Alignment.Caster, Fixtures.Ranges.Caster, "caster");
    }

    /* ---------------- ANTI-ROLL BARS ---------------- */
    [Fact]
    public void ArbFront_ExactAndInRange()
    {
        Assert.Equal(15.94, L.Arb.Front);
        Assert.Equal(26.04, E.Arb.Front);
        Assert.Equal(11.29, M.Arb.Front); // M oversteer-prone (mid-engine, 43% front): firmer front bar
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Arb.Front, Fixtures.Ranges.Arb, "arb.front");
    }

    [Fact]
    public void ArbRear_ExactAndInRange()
    {
        Assert.Equal(12.94, L.Arb.Rear);
        Assert.Equal(27.33, E.Arb.Rear);
        Assert.Equal(13.4, M.Arb.Rear); // oversteer-prone: softer rear bar
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Arb.Rear, Fixtures.Ranges.Arb, "arb.rear");
    }

    /* ---------------- SPRINGS & RIDE HEIGHT ---------------- */
    [Fact]
    public void SpringsFront_ExactAndWithinPart()
    {
        Assert.Equal(379, L.Springs.Front);
        Assert.Equal(900, E.Springs.Front);
        Assert.Equal(676, M.Springs.Front);
        Helpers.InRange(L.Springs.Front, (LCar.SpringRateMinF!.Value, LCar.SpringRateMaxF!.Value), "L.springs.front");
        Helpers.InRange(E.Springs.Front, (ECar.SpringRateMinF!.Value, ECar.SpringRateMaxF!.Value), "E.springs.front");
        Helpers.InRange(M.Springs.Front, (MCar.SpringRateMinF!.Value, MCar.SpringRateMaxF!.Value), "M.springs.front");
    }

    [Fact]
    public void SpringsRear_ExactAndWithinPart()
    {
        Assert.Equal(240, L.Springs.Rear);
        Assert.Equal(900, E.Springs.Rear);
        Assert.Equal(786, M.Springs.Rear);
        Helpers.InRange(L.Springs.Rear, (LCar.SpringRateMinR!.Value, LCar.SpringRateMaxR!.Value), "L.springs.rear");
        Helpers.InRange(E.Springs.Rear, (ECar.SpringRateMinR!.Value, ECar.SpringRateMaxR!.Value), "E.springs.rear");
        Helpers.InRange(M.Springs.Rear, (MCar.SpringRateMinR!.Value, MCar.SpringRateMaxR!.Value), "M.springs.rear");
    }

    [Fact]
    public void SpringsRide_ExactAndWithinPart()
    {
        Assert.Equal(4.5, L.Springs.RideF);
        Assert.Equal(4.5, L.Springs.RideR);
        Assert.Equal(4.6, E.Springs.RideF);
        Assert.Equal(4.8, E.Springs.RideR);
        Assert.Equal(4.5, M.Springs.RideF);
        Assert.Equal(4.6, M.Springs.RideR);
        Helpers.AssertSpringInPart(L, LCar);
        Helpers.AssertSpringInPart(E, ECar);
        Helpers.AssertSpringInPart(M, MCar);
    }

    /* ---------------- DAMPING ---------------- */
    [Fact]
    public void DampingReboundF_ExactAndInRange()
    {
        Assert.Equal(9, L.Damping.ReboundF);
        Assert.Equal(10.8, E.Damping.ReboundF);
        Assert.Equal(9.1, M.Damping.ReboundF);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Damping.ReboundF, Fixtures.Ranges.Damping, "reboundF");
    }

    [Fact]
    public void DampingReboundR_ExactAndInRange()
    {
        Assert.Equal(8.7, L.Damping.ReboundR);
        Assert.Equal(10.6, E.Damping.ReboundR);
        Assert.Equal(9.5, M.Damping.ReboundR);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Damping.ReboundR, Fixtures.Ranges.Damping, "reboundR");
    }

    [Fact]
    public void DampingBumpF_ExactAndInRange()
    {
        Assert.Equal(5.4, L.Damping.BumpF);
        Assert.Equal(6.4, E.Damping.BumpF);
        Assert.Equal(5.5, M.Damping.BumpF);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Damping.BumpF, Fixtures.Ranges.Damping, "bumpF");
    }

    [Fact]
    public void DampingBumpR_ExactAndInRange()
    {
        Assert.Equal(5.3, L.Damping.BumpR);
        Assert.Equal(6.3, E.Damping.BumpR);
        Assert.Equal(6, M.Damping.BumpR);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Damping.BumpR, Fixtures.Ranges.Damping, "bumpR");
    }

    [Fact]
    public void DampingBumpIs40To70PctOfRebound_RatioGuard()
    {
        // M is a non-bypass goal/drivetrain (Circuit RWD), so the ratio guard holds.
        foreach (var (bump, reb) in new[] { (M.Damping.BumpF, M.Damping.ReboundF), (M.Damping.BumpR, M.Damping.ReboundR) })
        {
            double ratio = bump / reb;
            Assert.True(ratio >= 0.4 - 1e-9 && ratio <= 0.7 + 1e-9, $"bump/rebound ratio {ratio} out of 0.4–0.7");
        }
    }

    /* ---------------- AERO ---------------- */
    [Fact]
    public void Aero_NoAeroNotApplicable_FullAeroInRange()
    {
        Assert.False(L.Aero.Applicable);
        Assert.Null(L.Aero.Front);
        Assert.Null(L.Aero.Rear);

        Assert.True(E.Aero.Applicable);
        Assert.Equal(100, E.Aero.Front);
        Assert.Equal(15, E.Aero.Rear);

        Assert.True(M.Aero.Applicable);
        Assert.Equal(95, M.Aero.Front);
        Assert.Equal(90, M.Aero.Rear); // oversteer-prone mid-engine rear wing not trimmed
        foreach (var t in new[] { E, M })
        {
            Helpers.InRange(t.Aero.Front!.Value, Fixtures.Ranges.AeroPct, "aero.front");
            Helpers.InRange(t.Aero.Rear!.Value, Fixtures.Ranges.AeroPct, "aero.rear");
        }
    }

    /* ---------------- BRAKING ---------------- */
    [Fact]
    public void BrakingBalance_ExactAndInRange()
    {
        Assert.Equal(54, L.Braking.Balance);
        Assert.Equal(57, E.Braking.Balance);
        Assert.Equal(48, M.Braking.Balance);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Braking.Balance, Fixtures.Ranges.BrakeBalance, "braking.balance");
    }

    [Fact]
    public void BrakingPressure_ExactAndInRange()
    {
        Assert.Equal(105, L.Braking.Pressure);
        Assert.Equal(120, E.Braking.Pressure);
        Assert.Equal(110, M.Braking.Pressure);
        foreach (var t in new[] { L, E, M }) Helpers.InRange(t.Braking.Pressure, Fixtures.Ranges.BrakePressure, "braking.pressure");
    }

    /* ---------------- DIFFERENTIAL ---------------- */
    [Fact]
    public void DifferentialAccelDecel_ExactAndInRange_Rwd()
    {
        Assert.Equal(Drivetrain.RWD, L.Differential.Driveline);
        Assert.Equal(56, L.Differential.Accel);
        Assert.Equal(20, L.Differential.Decel);
        Assert.Equal(Drivetrain.RWD, M.Differential.Driveline);
        Assert.Equal(38, M.Differential.Accel);
        Assert.Equal(20, M.Differential.Decel);
        foreach (var t in new[] { L, M })
        {
            Helpers.InRange(t.Differential.Accel, Fixtures.Ranges.Diff, "diff.accel");
            Helpers.InRange(t.Differential.Decel, Fixtures.Ranges.Diff, "diff.decel");
            Assert.Equal(0, t.Differential.Accel % 2); // accel must be an even %
        }
    }

    [Fact]
    public void DifferentialAwd_FullPerAxleSet_ExactAndInRange()
    {
        Assert.Equal(Drivetrain.AWD, E.Differential.Driveline);
        Assert.Equal(72, E.Differential.Accel);
        Assert.Equal(30, E.Differential.Decel);
        Assert.Equal(26, E.Differential.FrontAccel);
        Assert.Equal(5, E.Differential.FrontDecel);
        Assert.Equal(83, E.Differential.CenterRear);
        Helpers.InRange(E.Differential.Accel, Fixtures.Ranges.Diff, "rear accel");
        Helpers.InRange(E.Differential.Decel, Fixtures.Ranges.Diff, "rear decel");
        Helpers.InRange(E.Differential.FrontAccel!.Value, Fixtures.Ranges.Diff, "front accel");
        Helpers.InRange(E.Differential.FrontDecel!.Value, Fixtures.Ranges.Diff, "front decel");
        Helpers.InRange(E.Differential.CenterRear!.Value, Fixtures.Ranges.AwdCenter, "center rear");
    }

    /* ---------------- OVERSTEER-PRONE COMPENSATION ---------------- */
    private static readonly TuneInput OS = Fixtures.BaseInput(b =>
    {
        b.Drivetrain = Drivetrain.AWD;
        b.EngineLocation = EngineLocation.Rear;
        b.Powertrain = Powertrain.ICE;
        b.PiClass = PiClass.A;
        b.Power = 450;
        b.Torque = 369;
        b.Weight = 2614;
        b.FrontWeightPct = 46;
        b.Gears = 8;
        b.TireCompound = TireCompound.Street;
        b.AeroFront = new AeroRange(122, 203);
        b.AeroRear = new AeroRange(362, 702);
    });
    private static readonly Tune OSt = Fixtures.TUNING.Compute(OS, Goal.Circuit);

    [Fact]
    public void OversteerProne_RearEngineAwdFlagged_BalancedFrontEngineNot()
    {
        Assert.True(OSt.Derived.OversteerProne);
        var bal = Fixtures.TUNING.Compute(
            Fixtures.BaseInput(b => { b.Drivetrain = Drivetrain.AWD; b.EngineLocation = EngineLocation.Front; b.FrontWeightPct = 50; }),
            Goal.Circuit);
        Assert.False(bal.Derived.OversteerProne);
        Assert.Equal(15, bal.Aero.Rear); // balanced AWD keeps the min-rear-wing default
    }

    [Fact]
    public void OversteerProneAwd_CenterAndRearLocksPulledBack()
    {
        Assert.True(OSt.Differential.CenterRear!.Value <= 66, $"center {OSt.Differential.CenterRear} should be <= 66");
        Assert.True(OSt.Differential.Accel <= 55, $"rear accel {OSt.Differential.Accel} should be <= 55");
        Helpers.InRange(OSt.Differential.CenterRear!.Value, Fixtures.Ranges.AwdCenter, "center");
    }

    [Fact]
    public void OversteerProne_RollStiffnessShiftedForward()
    {
        Assert.True(OSt.Arb.Rear <= OSt.Arb.Front + 0.5, $"rear ARB {OSt.Arb.Rear} must not exceed front {OSt.Arb.Front}");
    }

    [Fact]
    public void OversteerProneAwd_RearWingPlanted_AeroBalanceRearBiased()
    {
        Assert.True(OSt.Aero.Rear!.Value >= 80, $"rear wing {OSt.Aero.Rear}% should be raised, not floored");
        Assert.True(OSt.Aero.RearLbf!.Value > OSt.Aero.FrontLbf!.Value, $"rear DF {OSt.Aero.RearLbf} must exceed front {OSt.Aero.FrontLbf}");
    }

    /* ---------------- WHOLE-TUNE INVARIANTS ---------------- */
    [Fact]
    public void EveryNumericOutputInRange_AllThreeCars()
    {
        Helpers.AssertAllInRange(L);
        Helpers.AssertAllInRange(E);
        Helpers.AssertAllInRange(M);
    }

    [Fact]
    public void EverySectionCarriesWhy_AllThreeCars()
    {
        Helpers.AssertWhyShape(L);
        Helpers.AssertWhyShape(E);
        Helpers.AssertWhyShape(M);
    }

    [Fact]
    public void SummaryStripAndDerivedPopulated()
    {
        foreach (var (t, car) in new[] { (L, LCar), (E, ECar), (M, MCar) })
        {
            Assert.Equal(5, t.Summary.Count);
            Assert.IsType<double>(t.Derived.Pw);
            var pwChip = t.Summary.First(s => s.K == "Power-to-weight");
            double pw = JsMath.Round(car.Power / car.Weight * 100) / 100;
            string pwStr = pw.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.StartsWith(pwStr, pwChip.V);
        }
    }

    /* ---------------- R CLASS (between S2 and X) ---------------- */
    [Fact]
    public void PiClassR_ResolvesOwnLadderSlot_ClassTierRace()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.PiClass = PiClass.R), Goal.Circuit);
        Assert.Equal(6, t.Derived.PiIdx);
        Assert.Equal(ClassTier.Race, t.Derived.ClassTier);
        Helpers.AssertAllInRange(t);
    }

    [Fact]
    public void PiClassR_PerClassMathInterpolatesBetweenS2AndX()
    {
        double Caster(PiClass pi) => Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.PiClass = pi; b.Weight = 3000; }), Goal.Circuit).Alignment.Caster;
        double Arb(PiClass pi) => Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.PiClass = pi; b.Weight = 3000; }), Goal.Circuit).Arb.Front;
        Assert.True(Caster(PiClass.S2) <= Caster(PiClass.R) && Caster(PiClass.R) <= Caster(PiClass.X),
            $"caster S2({Caster(PiClass.S2)}) ≤ R({Caster(PiClass.R)}) ≤ X({Caster(PiClass.X)})");
        Assert.True(Caster(PiClass.R) > Caster(PiClass.A), "R caster must exceed A (proves no A fallback)");
        Assert.True(Arb(PiClass.S2) >= Arb(PiClass.R) && Arb(PiClass.R) >= Arb(PiClass.X),
            $"arb.front S2({Arb(PiClass.S2)}) ≥ R({Arb(PiClass.R)}) ≥ X({Arb(PiClass.X)})");
    }
}
