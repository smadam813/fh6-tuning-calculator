using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// EDGE TESTS — structural behaviours at the boundaries of the model (the C# port of
/// <c>legacy/test/edge.test.js</c>): per-drivetrain differential shape, EV single-speed gearing,
/// rear-engine weight inversion, no-aero suppression, single-wing aero, aero lbf mapping, slider-at-0
/// identity, stock-suspension alignment/ARB lock, and independent front/rear part ranges.
///
/// <para>Note: where the JS asserts <c>"frontAccel" in diff === false</c> (key omitted), the C#
/// <see cref="Differential"/> models the same shape contract with a nullable field — so FWD/RWD have
/// <c>FrontAccel/FrontDecel/CenterRear == null</c> and only AWD populates them.</para>
/// </summary>
public sealed class EdgeTests
{
    /* ---------------- per-drivetrain differential shape ---------------- */
    [Fact]
    public void Fwd_FrontOnlyDifferential_NoRearOrCenterFields()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.Drivetrain = Drivetrain.FWD; b.FrontWeightPct = 62; }), Goal.Circuit);
        Assert.Equal(Drivetrain.FWD, t.Differential.Driveline);
        // accel/decel are always present numerics
        Helpers.InRange(t.Differential.Accel, Fixtures.Ranges.Diff, "FWD accel");
        Assert.InRange(t.Differential.Decel, 5, 100);
        // rear-axle / center fields absent (modeled as null)
        Assert.Null(t.Differential.FrontAccel);
        Assert.Null(t.Differential.FrontDecel);
        Assert.Null(t.Differential.CenterRear);
    }

    [Fact]
    public void Rwd_RearOnlyDifferential_NoCenterField()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.Drivetrain = Drivetrain.RWD), Goal.Circuit);
        Assert.Equal(Drivetrain.RWD, t.Differential.Driveline);
        Assert.Null(t.Differential.CenterRear);
        Assert.Null(t.Differential.FrontAccel);
    }

    [Fact]
    public void Awd_FullPerAxleDiffIncludingCenter()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.Drivetrain = Drivetrain.AWD), Goal.Circuit);
        Assert.Equal(Drivetrain.AWD, t.Differential.Driveline);
        Assert.NotNull(t.Differential.FrontAccel);
        Assert.NotNull(t.Differential.FrontDecel);
        Assert.NotNull(t.Differential.CenterRear);
        Helpers.InRange(t.Differential.CenterRear!.Value, Fixtures.Ranges.AwdCenter, "AWD centerRear");
        Assert.True(t.Differential.CenterRear!.Value >= 50, "AWD center never below 50% rear");
    }

    /* ---------------- EV single-speed gear logic ---------------- */
    [Fact]
    public void Ev_SingleSpeedRegardlessOfGears()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.Powertrain = Powertrain.EV; b.Gears = 8; }), Goal.Circuit);
        Assert.True(t.Gearing.SingleSpeed);
        Assert.Single(t.Gearing.Ratios);
        Helpers.InRange(t.Gearing.Ratios[0], Fixtures.Ranges.Gear, "EV ratio");
        Helpers.InRange(t.Gearing.Final, Fixtures.Ranges.Fd, "EV final");
    }

    [Fact]
    public void Ev_TargetSolveGearsLimiter7PctPastTarget()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b =>
        { b.Powertrain = Powertrain.EV; b.RedlineRpm = 9000; b.TireDiameter = 26; b.TargetTopSpeed = 180; }), Goal.Circuit);
        Assert.Equal("target", t.Gearing.FdSource);
        Assert.True(t.Gearing.SingleSpeed);
        Assert.True(Math.Abs(t.Gearing.TopSpeed!.Value - 180 * 1.07) < 2.5, $"EV redline speed {t.Gearing.TopSpeed} ~ {180 * 1.07}");
        Assert.True(t.Gearing.TopSpeed!.Value > 185, $"redline speed {t.Gearing.TopSpeed} must exceed target (headroom)");
    }

    [Fact]
    public void NonEvIce_MultiGearBoxMatchesGearsField()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.Powertrain = Powertrain.ICE; b.Gears = 6; }), Goal.Circuit);
        Assert.False(t.Gearing.SingleSpeed);
        Assert.Equal(6, t.Gearing.Ratios.Count);
    }

    /* ---------------- rear-engine weight inversion ---------------- */
    [Fact]
    public void RearEngine_RearBiasedWeightInSummary()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.EngineLocation = EngineLocation.Rear; b.FrontWeightPct = 40; }), Goal.Circuit);
        string bal = t.Summary.First(s => s.K == "Balance").V;
        Assert.Equal("40/60", bal);
        double frontCorner = JsNumber.ParseFloat(t.Summary.First(s => s.K == "Front corner").V);
        double rearCorner = JsNumber.ParseFloat(t.Summary.First(s => s.K == "Rear corner").V);
        Assert.True(rearCorner > frontCorner, "rear-engine: rear corner heavier than front");
    }

    [Fact]
    public void RearEngine_ShiftsEffectiveFrontWeightDown()
    {
        var front = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.EngineLocation = EngineLocation.Front; b.FrontWeightPct = 50; b.Drivetrain = Drivetrain.RWD; }), Goal.Circuit);
        var rear = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.EngineLocation = EngineLocation.Rear; b.FrontWeightPct = 50; b.Drivetrain = Drivetrain.RWD; }), Goal.Circuit);
        Assert.False(front.Alignment.CamberF == rear.Alignment.CamberF && front.Alignment.CamberR == rear.Alignment.CamberR,
            "engine location should change the alignment");
        Assert.True(rear.Braking.Balance <= front.Braking.Balance, "rear-engine brake bias <= front-engine");
    }

    /* ---------------- no-aero suppression ---------------- */
    [Fact]
    public void NoAero_SuppressesAero()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.HasFrontAero = false; b.HasRearAero = false; b.AeroInstalled = false; }), Goal.Circuit);
        Assert.False(t.Aero.Applicable);
        Assert.Null(t.Aero.Front);
        Assert.Null(t.Aero.Rear);
    }

    [Fact]
    public void FrontSplitterOnly_FrontValueNullRear()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.HasFrontAero = true; b.HasRearAero = false; }), Goal.Circuit);
        Assert.True(t.Aero.Applicable);
        Assert.NotNull(t.Aero.Front);
        Assert.Null(t.Aero.Rear);
        Helpers.InRange(t.Aero.Front!.Value, Fixtures.Ranges.AeroPct, "front-only DF");
    }

    [Fact]
    public void RearWingOnly_RearValueNullFront()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.HasFrontAero = false; b.HasRearAero = true; }), Goal.Circuit);
        Assert.True(t.Aero.Applicable);
        Assert.Null(t.Aero.Front);
        Assert.NotNull(t.Aero.Rear);
        Helpers.InRange(t.Aero.Rear!.Value, Fixtures.Ranges.AeroPct, "rear-only DF");
    }

    [Fact]
    public void DragGoal_ZeroesAeroEvenWithFullKit()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.HasFrontAero = true; b.HasRearAero = true; }), Goal.Drag);
        Assert.Equal(0, t.Aero.Front);
        Assert.Equal(0, t.Aero.Rear);
    }

    [Fact]
    public void Aero_MapsIntoEnteredLbfRange()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.AeroFront = new AeroRange(50, 300); b.AeroRear = new AeroRange(80, 400); }), Goal.Circuit);
        Assert.True(t.Aero.FrontLbf!.Value >= 50 && t.Aero.FrontLbf!.Value <= 300, "frontLbf within entered range");
        Assert.True(t.Aero.RearLbf!.Value >= 80 && t.Aero.RearLbf!.Value <= 400, "rearLbf within entered range");
        Assert.True(t.Aero.FrontLbf!.Value > 50, "non-zero front DF maps above min");
    }

    /* ---------------- slider at 0 == baseline (whole tune) ---------------- */
    [Fact]
    public void HandlingBias0_IdenticalToOmittingBias_PerGoal()
    {
        foreach (Goal g in Fixtures.TUNING.Goals)
        {
            var withZero = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.HandlingBias = 0), g);
            var omitted = Fixtures.TUNING.Compute(Fixtures.BaseInput(), g); // default HandlingBias is 0 (== JS undefined → 0)
            Assert.Equal(Strip(withZero), Strip(omitted));
        }
    }

    /* ---------------- stock suspension locks alignment/ARB ---------------- */
    [Fact]
    public void StockSuspension_LocksAlignmentAndCentresArbs()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.SuspensionType = SuspensionType.Stock), Goal.Circuit);
        Assert.Equal(0, t.Alignment.CamberF);
        Assert.Equal(0, t.Alignment.CamberR);
        Assert.Equal(0, t.Alignment.ToeF);
        Assert.Equal(0, t.Alignment.ToeR);
        Assert.Equal(32.5, t.Arb.Front);
        Assert.Equal(32.5, t.Arb.Rear);
        Helpers.AssertAllInRange(t);
    }

    /* ---------------- independent front/rear part ranges ---------------- */
    [Fact]
    public void AsymmetricPartRanges_ClampEachAxleToOwnSpan()
    {
        var car = Fixtures.BaseInput(b =>
        {
            b.Weight = 1800;
            b.SpringRateMinF = 150; b.SpringRateMaxF = 400;
            b.SpringRateMinR = 600; b.SpringRateMaxR = 1400;
            b.RideHeightMinF = 3.0; b.RideHeightMaxF = 4.0;
            b.RideHeightMinR = 6.0; b.RideHeightMaxR = 9.0;
        });
        var t = Fixtures.TUNING.Compute(car, Goal.OffRoad);
        Helpers.InRange(t.Springs.Front, (150, 400), "springs.front in FRONT range");
        Helpers.InRange(t.Springs.Rear, (600, 1400), "springs.rear in REAR range");
        Assert.True(t.Springs.Rear > 400, "rear spring uses the higher rear range");
        Assert.True(t.Springs.Front < 600, "front spring uses the lower front range");
        Assert.Equal(4.0, t.Springs.RideF);
        Assert.Equal(9.0, t.Springs.RideR);
        Helpers.InRange(t.Springs.RideF, (3.0, 4.0), "rideF in FRONT range");
        Helpers.InRange(t.Springs.RideR, (6.0, 9.0), "rideR in REAR range");
    }

    // Strip helper for the dial-0 identity test (same sections the JS strip(...) keeps).
    private static string Strip(Tune t) => System.Text.Json.JsonSerializer.Serialize(new
    {
        tires = t.Tires,
        gearing = t.Gearing,
        alignment = t.Alignment,
        arb = t.Arb,
        springs = new { f = t.Springs.Front, r = t.Springs.Rear, rf = t.Springs.RideF, rr = t.Springs.RideR },
        damping = t.Damping,
        aero = new { f = t.Aero.Front, r = t.Aero.Rear },
        braking = t.Braking,
        differential = t.Differential,
    }, ParityHarness.RoundTrip);
}
