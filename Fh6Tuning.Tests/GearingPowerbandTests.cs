using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// Powerband-aware gearing: <b>peak-power RPM</b> caps the final-drive top-speed back-solve and
/// <b>max-torque RPM</b> drives per-gear spacing. These optional inputs are deliberately absent from
/// the 2340-case parity grid (so the byte-for-byte gate proves the change is a no-op there but cannot
/// cover this path) — hence these C#-native tests are the primary correctness + JS/C# bit-drift guard.
/// Expected values are ground truth produced by the legacy oracle (<c>legacy/tuning.js</c>).
/// </summary>
public sealed class GearingPowerbandTests
{
    private static readonly ITuningEngine Engine = new TuningEngine();

    /// <summary>2005 Porsche Cayman GT3 WTAC — the reported car. Note peak power 8900 sits ABOVE the
    /// 8700 redline (unreachable; FH6 power fades near the limiter) and max torque is a high 7800.</summary>
    private static TuneInput Cayman(Action<TuneInputBuilder>? extra = null) => Fixtures.BaseInput(b =>
    {
        b.Drivetrain = Drivetrain.AWD; b.EngineLocation = EngineLocation.Mid; b.Powertrain = Powertrain.ICE;
        b.PiClass = PiClass.S2; b.TireCompound = TireCompound.Race; b.SuspensionType = SuspensionType.Race;
        b.Power = 630; b.Torque = 410; b.Weight = 2258; b.FrontWeightPct = 47; b.Gears = 7;
        b.HasFrontAero = true; b.HasRearAero = true; b.AeroInstalled = true;
        b.RedlineRpm = 8700; b.TargetTopSpeed = 193;
        b.TireDiameter = 19 + 2 * (365 * 0.30) / 25.4; // 365/30R19 -> 27.622 in
        b.PeakPowerRpm = 8900; b.MaxTorqueRpm = 7800;
        extra?.Invoke(b);
    });

    private static void AssertDescendingInRange(IReadOnlyList<double> r, string ctx)
    {
        Assert.NotEmpty(r);
        foreach (var g in r)
        {
            Assert.True(double.IsFinite(g), $"non-finite ratio @ {ctx}");
            Assert.InRange(g, 0.5, 5.5);
        }
        for (int k = 1; k < r.Count; k++)
            Assert.True(r[k] < r[k - 1], $"gears not strictly descending @ {ctx}: {string.Join(",", r)}");
    }

    private static void AssertRatios(double[] expected, IReadOnlyList<double> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int k = 0; k < expected.Length; k++)
            Assert.True(Math.Abs(actual[k] - expected[k]) < 1e-6, $"gear {k + 1}: {actual[k]} != {expected[k]}");
    }

    private const double CaymanTireDia = 19 + 2 * (365 * 0.30) / 25.4; // 365/30R19 -> 27.622 in

    // 1st-gear top speed at redline (mph) — the launchability proxy. Lower = shorter 1st = launches harder;
    // a too-tall 1st (high mph here) bogs off the line.
    private static double FirstGearTopMph(Gearing g, double redlineRpm = 8700, double tireDiaIn = CaymanTireDia)
        => redlineRpm * Math.PI * tireDiaIn * 60 / 63360 / (g.Ratios[0] * g.Final);

    [Fact]
    public void Cayman_PowerbandTune_DropGearLaunchesAndKeeps193()
    {
        var g = Engine.Compute(Cayman(), Goal.Circuit).Gearing;
        Assert.Equal("target", g.FdSource);
        // Narrow high band (redline/maxTq = 1.12): 1st is a launch DROP gear, gears 2..7 a tight cluster.
        AssertRatios([4.06, 2.01, 1.68, 1.47, 1.33, 1.23, 1.14], g.Ratios);
        Assert.True(Math.Abs(g.Final - 2.95) < 1e-6, $"final {g.Final} ~ 2.95");
        // 1st now tops ~59.7 mph at redline (launchable) instead of the 87.8 mph bog before the drop gear.
        Assert.InRange(FirstGearTopMph(g), 55.0, 63.0);
        // The 1->2 gap is a real drop gear (>1.7x); gears 2..7 stay the tight power-band cluster (~1.20x steps).
        Assert.True(g.Ratios[0] / g.Ratios[1] > 1.7, $"1->2 drop {g.Ratios[0] / g.Ratios[1]} should be a real drop gear");
        Assert.True(g.Ratios[1] / g.Ratios[2] < 1.35, "cluster step 2->3 stays tight");
        // FD x topGear ~ 3.37 -> still ~193 mph; displayed @redline top exceeds 193 (headroom).
        Assert.True(g.TopSpeed!.Value > 193, $"displayed @redline top {g.TopSpeed} exceeds target (headroom)");
    }

    [Fact]
    public void Cayman_PeakPowerAboveRedline_IsCappedNotUsedLiterally()
    {
        // Peak power 8900 (> redline) must be capped by the droop estimate, so it produces the SAME FD
        // as an absurdly high peak — and NOT the over-short FD that gearing literally to 8900 would give.
        double fd8900 = Engine.Compute(Cayman(b => b.PeakPowerRpm = 8900), Goal.Circuit).Gearing.Final;
        double fd20000 = Engine.Compute(Cayman(b => b.PeakPowerRpm = 20000), Goal.Circuit).Gearing.Final;
        double fdEstimate = Engine.Compute(Cayman(b => b.PeakPowerRpm = null), Goal.Circuit).Gearing.Final;
        Assert.Equal(fd20000, fd8900);   // both capped to the same droop-aware effective rpm
        Assert.Equal(fdEstimate, fd8900); // == the estimate-only result (peak power didn't shorten it)
    }

    [Fact]
    public void Cayman_NoPowerbandInputs_FallsBackToCurrentSpacingAndEstimateFd()
    {
        var g = Engine.Compute(Cayman(b => { b.PeakPowerRpm = null; b.MaxTorqueRpm = null; }), Goal.Circuit).Gearing;
        // max-torque absent -> current Rn = A.n^B spacing (byte-identical to pre-change);
        // peak-power absent -> the droop-aware hp/torque estimate, which alone fixes top speed to FD 4.33.
        AssertRatios([2.75, 1.75, 1.34, 1.12, 0.96, 0.86, 0.78], g.Ratios);
        Assert.True(Math.Abs(g.Final - 4.33) < 1e-6, $"final {g.Final} ~ 4.33 (the user's measured on-target FD)");
    }

    [Fact]
    public void MaxTorqueRpm_SpacesTighterForNarrowerBand()
    {
        // Same car, only the band width differs: narrow band -> closer ratios -> taller (higher) top gear.
        double wideTop = Engine.Compute(Cayman(b => b.MaxTorqueRpm = 3500), Goal.Circuit).Gearing.Ratios[^1];
        double narrowTop = Engine.Compute(Cayman(b => b.MaxTorqueRpm = 7800), Goal.Circuit).Gearing.Ratios[^1];
        Assert.True(narrowTop > wideTop, $"narrow-band top gear {narrowTop} should exceed wide-band {wideTop}");
    }

    public static TheoryData<string, double, double, double, int> EdgeCases() => new()
    {
        // label, power/weight-ish via power+weight set in builder; here: power, torque, redline, maxTorque, gears
        { "wide-flat-band", 480, 7000, 3000, 6 },
        { "peaky-narrow-band", 210, 9000, 7200, 6 },
        { "few-gears-drag", 600, 7500, 4500, 4 },
        { "ten-speed", 700, 8000, 5000, 10 },
        { "max-torque-ge-redline", 300, 7000, 9000, 6 },
    };

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void EdgeBandsAndGearCounts_StayStrictlyDescendingAndInRange(string label, double torque, double redline, double maxTorque, int gears)
    {
        foreach (Goal goal in Engine.Goals)
        {
            var g = Engine.Compute(Fixtures.BaseInput(b =>
            {
                b.Power = 500; b.Torque = torque; b.Weight = 3000; b.Gears = gears;
                b.RedlineRpm = redline; b.MaxTorqueRpm = maxTorque; b.PeakPowerRpm = redline * 0.9;
                b.TireDiameter = 27; b.TargetTopSpeed = 200;
            }), goal).Gearing;
            AssertDescendingInRange(g.Ratios, $"{label}/{goal}");
            Assert.InRange(g.Final, 2.0, 7.0);
        }
    }

    [Fact]
    public void ZeroTorque_DoesNotProduceNaN()
    {
        // torque <= 0 must not 0/0 the estimate; effRpm falls back to 0.95*redline.
        var g = Engine.Compute(Fixtures.BaseInput(b =>
        {
            b.Power = 400; b.Torque = 0; b.Weight = 3000; b.Gears = 6;
            b.RedlineRpm = 7000; b.MaxTorqueRpm = 5000; b.TireDiameter = 26; b.TargetTopSpeed = 160;
        }), Goal.Circuit).Gearing;
        Assert.Equal("target", g.FdSource);
        Assert.True(double.IsFinite(g.Final));
        AssertDescendingInRange(g.Ratios, "zero-torque");
    }

    [Fact]
    public void AllGearCounts_AllBandWidths_StayValid()
    {
        // The user's explicit concern: every gear count 2..10 across wide/narrow bands stays valid.
        double[] maxTorques = { 2500, 3500, 5000, 6500, 7500 }; // redline 8000 -> bands 3.2 .. 1.07
        for (int gears = 2; gears <= 10; gears++)
            foreach (double mt in maxTorques)
                foreach (Goal goal in Engine.Goals)
                {
                    var g = Engine.Compute(Fixtures.BaseInput(b =>
                    {
                        b.Power = 600; b.Torque = 450; b.Weight = 3000; b.Gears = gears;
                        b.RedlineRpm = 8000; b.MaxTorqueRpm = mt; b.PeakPowerRpm = 7000;
                        b.TireDiameter = 27; b.TargetTopSpeed = 200;
                    }), goal).Gearing;
                    AssertDescendingInRange(g.Ratios, $"N={gears}/maxTq={mt}/{goal}");
                    Assert.Equal(gears, g.Ratios.Count);
                    Assert.InRange(g.Final, 2.0, 7.0);
                }
    }

    [Fact]
    public void Cayman_6Speed_DropGearStillLaunches()
    {
        var g = Engine.Compute(Cayman(b => b.Gears = 6), Goal.Circuit).Gearing;
        Assert.Equal("target", g.FdSource);
        AssertRatios([3.92, 2.01, 1.67, 1.47, 1.33, 1.22], g.Ratios);
        Assert.True(Math.Abs(g.Final - 2.76) < 1e-6, $"final {g.Final} ~ 2.76");
        // 6-speed 1st tops ~66 mph at redline — launchable (was a 94.5 mph bog before the drop gear).
        Assert.InRange(FirstGearTopMph(g), 60.0, 68.0);
        Assert.True(g.TopSpeed!.Value > 193, "still hits ~193 (headroom past peak power)");
    }

    [Fact]
    public void Cayman_MoreGearsNeverBogsLaunch()
    {
        // The exact inverse of the reported bug (where the 7-speed bogged WORSE than the 6-speed):
        // overall 1st (gear x FD) must be non-decreasing in gear count, so 1st@redline only falls
        // (more launchable) as gears are added.
        double prevOverall1st = 0;
        for (int gears = 2; gears <= 10; gears++)
        {
            var g = Engine.Compute(Cayman(b => b.Gears = gears), Goal.Circuit).Gearing;
            double overall1st = g.Ratios[0] * g.Final;
            Assert.True(overall1st >= prevOverall1st - 1e-9,
                $"N={gears} overall_1st {overall1st} regressed vs N={gears - 1} ({prevOverall1st}) — adding gears must not bog launch");
            prevOverall1st = overall1st;
            AssertDescendingInRange(g.Ratios, $"cayman N={gears}");
        }
    }

    [Fact]
    public void WideBand_KeepsShippedAnBoxNoDropGear()
    {
        // A wide band (goalBeff >= B) must NOT engage the drop gear — 1st stays the shipped A-based ratio
        // (== A = 2.75 for this car), so wide-band cars are byte-identical to the pre-drop-gear behavior.
        var g = Engine.Compute(Cayman(b => b.MaxTorqueRpm = 3500), Goal.Circuit).Gearing;
        Assert.Equal("target", g.FdSource);
        Assert.True(Math.Abs(g.Ratios[0] - 2.75) < 1e-6, $"wide-band 1st {g.Ratios[0]} should be the shipped A (2.75), not a drop gear");
        AssertDescendingInRange(g.Ratios, "wide-band Cayman");
    }
}
