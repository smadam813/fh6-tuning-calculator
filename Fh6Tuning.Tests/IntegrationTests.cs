using System.Text.Json;
using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// INTEGRATION TESTS — one base car, all six goals, plus both dial sliders (the C# port of
/// <c>legacy/test/integration.test.js</c>). Asserts goal-specific parameters move in the expected
/// DIRECTION, that the handling-bias and overall-stiffness dials produce distinct, correctly-ordered
/// output sets, that dial==0 equals the pre-slider baseline byte-for-byte, that stiffness leaves
/// balance levers untouched and is exempt under stock suspension, and the optional gearing physics.
/// </summary>
public sealed class IntegrationTests
{
    private static readonly TuneInput BASE = Fixtures.BaseInput();
    private static readonly Dictionary<Goal, Tune> G = BuildGoals();

    private static Dictionary<Goal, Tune> BuildGoals()
    {
        var map = new Dictionary<Goal, Tune>();
        foreach (Goal g in Fixtures.TUNING.Goals) map[g] = Fixtures.TUNING.Compute(BASE, g);
        return map;
    }

    // Byte-for-byte comparison helper: serialize the dial-affected sections to canonical JSON and
    // compare the strings (the C# analog of the JS `strip(...)` + deepEqual).
    private static string Strip(Tune t) => JsonSerializer.Serialize(new
    {
        tires = t.Tires,
        gearing = t.Gearing,
        alignment = t.Alignment,
        arb = t.Arb,
        springs = new { t.Springs.Front, t.Springs.Rear, t.Springs.RideF, t.Springs.RideR },
        damping = t.Damping,
        aero = new { t.Aero.Front, t.Aero.Rear },
        braking = t.Braking,
        differential = t.Differential,
    }, ParityHarness.RoundTrip);

    /* ---------------- every goal produces a legal tune ---------------- */
    [Fact]
    public void AllSixGoals_YieldInRangeTune()
    {
        foreach (Goal g in Fixtures.TUNING.Goals) Helpers.AssertAllInRange(G[g]);
    }

    /* ---------------- goal-specific directional assertions ---------------- */
    [Fact]
    public void CircuitArbStifferThanOffRoad_BothEnds()
    {
        Assert.True(G[Goal.Circuit].Arb.Front > G[Goal.OffRoad].Arb.Front, "front ARB Circuit > OffRoad");
        Assert.True(G[Goal.Circuit].Arb.Rear > G[Goal.OffRoad].Arb.Rear, "rear ARB Circuit > OffRoad");
    }

    [Fact]
    public void OffRoadRidesHigherThanCircuit()
    {
        Assert.True(G[Goal.OffRoad].Springs.RideF > G[Goal.Circuit].Springs.RideF, "OffRoad rideF taller");
        Assert.True(G[Goal.OffRoad].Springs.RideR > G[Goal.Circuit].Springs.RideR, "OffRoad rideR taller");
    }

    [Fact]
    public void OffRoadRallySpringsSofterThanCircuit()
    {
        Assert.True(G[Goal.OffRoad].Springs.Front < G[Goal.Circuit].Springs.Front, "OffRoad softer front");
        Assert.True(G[Goal.Rally].Springs.Front < G[Goal.Circuit].Springs.Front, "Rally softer front");
    }

    [Fact]
    public void CircuitMoreDownforceThanDrag_Zeroed()
    {
        Assert.Equal(0, G[Goal.Drag].Aero.Front);
        Assert.Equal(0, G[Goal.Drag].Aero.Rear);
        Assert.True(G[Goal.Circuit].Aero.Front > G[Goal.Drag].Aero.Front, "Circuit front DF > Drag");
        Assert.True(G[Goal.Circuit].Aero.Rear > G[Goal.Drag].Aero.Rear, "Circuit rear DF > Drag");
    }

    [Fact]
    public void CircuitDownforceGreaterEqualTouge()
    {
        Assert.True(G[Goal.Circuit].Aero.Front >= G[Goal.Touge].Aero.Front, "Circuit front DF >= Touge");
        Assert.True(G[Goal.Circuit].Aero.Rear >= G[Goal.Touge].Aero.Rear, "Circuit rear DF >= Touge");
    }

    [Fact]
    public void DriftStifferRearArbThanFront()
    {
        Assert.True(G[Goal.Drift].Arb.Rear > G[Goal.Drift].Arb.Front * 1.5, "Drift rear ARB >> front ARB");
    }

    [Fact]
    public void DriftFrontCamberFarMoreNegativeThanCircuit()
    {
        Assert.True(G[Goal.Drift].Alignment.CamberF < G[Goal.Circuit].Alignment.CamberF - 0.5, "Drift camberF much more negative");
    }

    [Fact]
    public void DriftBrakeBalanceRearBiased()
    {
        Assert.Equal(48, G[Goal.Drift].Braking.Balance);
        Assert.True(G[Goal.Drift].Braking.Balance < G[Goal.Circuit].Braking.Balance, "Drift more rearward than Circuit");
    }

    [Fact]
    public void DragDifferentialAccelLocksRearHarderThanCircuit()
    {
        Assert.True(G[Goal.Drag].Differential.Accel > G[Goal.Circuit].Differential.Accel, "Drag higher accel lock");
    }

    [Fact]
    public void OffRoadBrakePressureEasedVsCircuit()
    {
        Assert.True(G[Goal.OffRoad].Braking.Pressure < G[Goal.Circuit].Braking.Pressure, "OffRoad softer braking");
    }

    [Fact]
    public void OffRoadRallyFinalDriveShorterThanCircuit()
    {
        Assert.True(G[Goal.OffRoad].Gearing.Final > G[Goal.Circuit].Gearing.Final, "OffRoad FD shorter");
        Assert.True(G[Goal.Rally].Gearing.Final > G[Goal.Circuit].Gearing.Final, "Rally FD shorter");
    }

    [Fact]
    public void AllSixGoals_MutuallyDistinct()
    {
        string Sig(Tune t) => JsonSerializer.Serialize(new object?[]
        {
            t.Arb.Front, t.Arb.Rear, t.Springs.Front, t.Springs.Rear,
            t.Aero.Front, t.Aero.Rear, t.Braking.Balance, t.Differential.Accel,
            t.Gearing.Final,
        });
        var seen = new HashSet<string>(Fixtures.TUNING.Goals.Select(g => Sig(G[g])));
        Assert.Equal(Fixtures.TUNING.Goals.Count, seen.Count);
    }

    /* ---------------- handling-bias slider ---------------- */
    private static readonly Tune BiasN5 = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.HandlingBias = -5), Goal.Circuit);
    private static readonly Tune Bias0 = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.HandlingBias = 0), Goal.Circuit);
    private static readonly Tune BiasP5 = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.HandlingBias = 5), Goal.Circuit);

    [Fact]
    public void Bias0_EqualsBaselineByteForByte()
    {
        Assert.Equal(Strip(G[Goal.Circuit]), Strip(Bias0));
    }

    [Fact]
    public void BiasN5_0_P5_ThreeDistinctSets()
    {
        string Sig(Tune t) => JsonSerializer.Serialize(new object?[]
        { t.Arb.Front, t.Arb.Rear, t.Springs.Front, t.Springs.Rear, t.Braking.Balance, t.Differential.Accel, t.Aero.Front, t.Aero.Rear });
        var s = new HashSet<string> { Sig(BiasN5), Sig(Bias0), Sig(BiasP5) };
        Assert.Equal(3, s.Count);
    }

    [Fact]
    public void Bias_CorrectlyOrdered()
    {
        // front ARB: + softens → descending across −5, 0, +5
        Assert.True(BiasN5.Arb.Front > Bias0.Arb.Front, "−5 front ARB stiffest");
        Assert.True(Bias0.Arb.Front > BiasP5.Arb.Front, "+5 front ARB softest");
        // rear ARB ascending
        Assert.True(BiasN5.Arb.Rear < Bias0.Arb.Rear, "−5 rear ARB softest");
        Assert.True(Bias0.Arb.Rear < BiasP5.Arb.Rear, "+5 rear ARB stiffest");
        // front spring descends
        Assert.True(BiasN5.Springs.Front > Bias0.Springs.Front && Bias0.Springs.Front > BiasP5.Springs.Front, "front spring descends");
        // brake balance ascends
        Assert.True(BiasN5.Braking.Balance < Bias0.Braking.Balance && Bias0.Braking.Balance < BiasP5.Braking.Balance, "brake balance ascends");
        // diff accel ascends
        Assert.True(BiasN5.Differential.Accel < Bias0.Differential.Accel && Bias0.Differential.Accel < BiasP5.Differential.Accel, "diff accel ascends");
        // aero front ascends, rear descends
        Assert.True(BiasN5.Aero.Front < Bias0.Aero.Front && Bias0.Aero.Front < BiasP5.Aero.Front, "aero front ascends");
        Assert.True(BiasN5.Aero.Rear > Bias0.Aero.Rear && Bias0.Aero.Rear > BiasP5.Aero.Rear, "aero rear descends");
    }

    [Fact]
    public void BiasExtremes_StayInRange()
    {
        Helpers.AssertAllInRange(BiasN5);
        Helpers.AssertAllInRange(BiasP5);
    }

    [Fact]
    public void BiasNeverErasesGoalCharacter_DriftStaysDrift()
    {
        var driftBias = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.HandlingBias = 5), Goal.Drift);
        Assert.Equal(G[Goal.Drift].Differential.Accel, driftBias.Differential.Accel);
        Assert.Equal(G[Goal.Drift].Braking.Balance, driftBias.Braking.Balance);
    }

    [Fact]
    public void BiasMovesFwdFrontDifferentialInOrder()
    {
        Tune Fwd(double b) => Fixtures.TUNING.Compute(
            Fixtures.BaseInput(x => { x.Drivetrain = Drivetrain.FWD; x.FrontWeightPct = 60; x.HandlingBias = b; }), Goal.Circuit);
        var n = Fwd(-5); var z = Fwd(0); var p = Fwd(5);
        Assert.Equal(Drivetrain.FWD, z.Differential.Driveline);
        Assert.True(n.Differential.Accel < z.Differential.Accel && z.Differential.Accel < p.Differential.Accel, "FWD accel ascends with bias");
        Assert.True(p.Differential.Accel <= 95, "FWD accel stays <= 95");
        Assert.True(n.Differential.Decel >= 5, "FWD decel stays >= 5");
        Helpers.AssertAllInRange(p); Helpers.AssertAllInRange(n);
    }

    [Fact]
    public void BiasMovesAwdCenterTorqueSplitInOrder()
    {
        Tune Awd(double b) => Fixtures.TUNING.Compute(
            Fixtures.BaseInput(x => { x.Drivetrain = Drivetrain.AWD; x.HandlingBias = b; }), Goal.Circuit);
        var n = Awd(-5); var z = Awd(0); var p = Awd(5);
        Assert.True(n.Differential.CenterRear!.Value < z.Differential.CenterRear!.Value, "−5 sends torque forward");
        Assert.True(z.Differential.CenterRear!.Value < p.Differential.CenterRear!.Value, "+5 sends torque rearward");
        Assert.True(p.Differential.CenterRear!.Value <= 90 && n.Differential.CenterRear!.Value >= 50, "center clamped 50–90");
    }

    /* ---------------- overall-stiffness slider ---------------- */
    private static readonly Tune StiffN5 = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.OverallStiffness = -5), Goal.Rally);
    private static readonly Tune Stiff0 = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.OverallStiffness = 0), Goal.Rally);
    private static readonly Tune StiffP5 = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => b.OverallStiffness = 5), Goal.Rally);

    [Fact]
    public void Stiffness0_EqualsBaselineByteForByte()
    {
        Assert.Equal(Strip(Fixtures.TUNING.Compute(Fixtures.BaseInput(), Goal.Rally)), Strip(Stiff0));
    }

    [Fact]
    public void StiffnessN5_0_P5_ThreeDistinctSets()
    {
        string Sig(Tune t) => JsonSerializer.Serialize(new object?[]
        { t.Springs.Front, t.Springs.Rear, t.Springs.RideF, t.Arb.Front, t.Arb.Rear, t.Damping.BumpF, t.Damping.ReboundF });
        var s = new HashSet<string> { Sig(StiffN5), Sig(Stiff0), Sig(StiffP5) };
        Assert.Equal(3, s.Count);
    }

    [Fact]
    public void StiffnessFirmsBothEndsTogether()
    {
        Assert.True(StiffP5.Springs.Front > Stiff0.Springs.Front && StiffP5.Springs.Rear > Stiff0.Springs.Rear, "+5 stiffens both spring ends");
        Assert.True(StiffN5.Springs.Front < Stiff0.Springs.Front && StiffN5.Springs.Rear < Stiff0.Springs.Rear, "−5 softens both spring ends");
        Assert.True(StiffP5.Arb.Front > Stiff0.Arb.Front && StiffP5.Arb.Rear > Stiff0.Arb.Rear, "+5 stiffens both bars");
        Assert.True(StiffN5.Arb.Front < Stiff0.Arb.Front && StiffN5.Arb.Rear < Stiff0.Arb.Rear, "−5 softens both bars");
        Assert.True(StiffP5.Damping.ReboundF > Stiff0.Damping.ReboundF && StiffN5.Damping.ReboundF < Stiff0.Damping.ReboundF, "damping tracks the dial");
    }

    [Fact]
    public void HardLowersRideHeight_SoftRaises()
    {
        Assert.True(StiffP5.Springs.RideF < Stiff0.Springs.RideF && StiffP5.Springs.RideR < Stiff0.Springs.RideR, "+5 drops ride height");
        Assert.True(StiffN5.Springs.RideF > Stiff0.Springs.RideF && StiffN5.Springs.RideR > Stiff0.Springs.RideR, "−5 raises ride height");
    }

    [Fact]
    public void StiffnessLeavesBalanceLeversUntouched()
    {
        foreach (var t in new[] { StiffN5, StiffP5 })
        {
            Assert.Equal(Stiff0.Braking.Balance, t.Braking.Balance);
            Assert.Equal(Stiff0.Differential.Accel, t.Differential.Accel);
            Assert.Equal(Stiff0.Aero.Front, t.Aero.Front);
            Assert.Equal(Stiff0.Alignment.CamberF, t.Alignment.CamberF);
            Assert.Equal(Stiff0.Tires.Front, t.Tires.Front);
        }
    }

    [Fact]
    public void StiffnessExtremes_StayInRange()
    {
        Helpers.AssertAllInRange(StiffN5);
        Helpers.AssertAllInRange(StiffP5);
    }

    [Fact]
    public void StiffnessComposesWithHandlingBias_StaysInRange()
    {
        foreach (double sgn in new double[] { -5, 5 })
            foreach (double b in new double[] { -5, 5 })
                Helpers.AssertAllInRange(Fixtures.TUNING.Compute(
                    Fixtures.BaseInput(x => { x.OverallStiffness = sgn; x.HandlingBias = b; }), Goal.Circuit));
    }

    [Fact]
    public void StockSuspensionExemptsStiffnessDial()
    {
        var stock0 = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.SuspensionType = SuspensionType.Stock; b.OverallStiffness = 0; }), Goal.Circuit);
        var stockHard = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.SuspensionType = SuspensionType.Stock; b.OverallStiffness = 5; }), Goal.Circuit);
        Assert.Equal(stock0.Springs.Front, stockHard.Springs.Front);
        Assert.Equal(stock0.Arb.Front, stockHard.Arb.Front);
        Assert.Equal(stock0.Damping.BumpF, stockHard.Damping.BumpF);
    }

    /* ---------------- optional gearing physics (back-solved final drive) ---------------- */
    [Fact]
    public void NonEvGearing_BackSolvesFinalDriveToTargetTopSpeed()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.RedlineRpm = 7000; b.TireDiameter = 26; b.TargetTopSpeed = 160; }), Goal.Circuit);
        Assert.Equal("target", t.Gearing.FdSource);
        Assert.False(t.Gearing.SingleSpeed);
        Assert.NotNull(t.Gearing.Speeds);
        Assert.Equal((int)BASE.Gears, t.Gearing.Speeds!.Count);
        // Top speed is power-limited, reached at the effective peak-power rpm (no peakPowerRpm given →
        // the hp/torque estimate floors at 0.85×redline = 5950), not the 7000 redline. The displayed
        // top-gear speed is at the redline, so it sits above the target by redline/effRpm: 160×7000/5950.
        Assert.True(t.Gearing.TopSpeed!.Value > 160, $"displayed @redline top speed {t.Gearing.TopSpeed} exceeds target (headroom)");
        Assert.True(Math.Abs(t.Gearing.TopSpeed!.Value - 160 * 7000 / 5950) < 1.5, $"top speed {t.Gearing.TopSpeed} ~ 188.2");
    }

    [Fact]
    public void Gearing_WithRedlineAndTireButNoTarget_UsesHeuristicFd()
    {
        var t = Fixtures.TUNING.Compute(Fixtures.BaseInput(b => { b.RedlineRpm = 7000; b.TireDiameter = 26; }), Goal.Circuit);
        Assert.Equal("heuristic", t.Gearing.FdSource);
        Assert.NotNull(t.Gearing.Speeds);
        Assert.True(t.Gearing.TopSpeed!.Value > 0, "top speed positive");
    }
}
