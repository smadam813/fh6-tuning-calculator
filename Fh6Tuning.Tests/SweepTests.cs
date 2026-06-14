using System.Text.Json;
using System.Text.Json.Nodes;
using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// Broad invariant sweep — the xUnit equivalent of <c>legacy/sweep.js</c>. Fuzzes the full input
/// space (the SAME 27-config DT×PT×EL grid as the JS sweep) and asserts INVARIANTS rather than exact
/// values: no NaN / non-finite output; every numeric output inside its legal Forza slider range;
/// multi-gear ratios strictly descending; ride height within the part range; <c>handlingBias==0</c>
/// AND <c>overallStiffness==0</c> byte-for-byte identical to the baseline; each car's six goals
/// mutually distinct; varying only the drivetrain produces distinct tunes.
/// </summary>
public sealed class SweepTests
{
    private static readonly ITuningEngine Engine = new TuningEngine();

    private static readonly Drivetrain[] DT = [Drivetrain.FWD, Drivetrain.RWD, Drivetrain.AWD];
    private static readonly Powertrain[] PT = [Powertrain.ICE, Powertrain.EV, Powertrain.Hybrid];
    private static readonly EngineLocation[] EL = [EngineLocation.Front, EngineLocation.Mid, EngineLocation.Rear];
    private static readonly PiClass[] PI = [PiClass.D, PiClass.C, PiClass.B, PiClass.A, PiClass.S1, PiClass.S2, PiClass.R, PiClass.X];
    private static readonly TireCompound[] TC = [TireCompound.Stock, TireCompound.Street, TireCompound.Sport, TireCompound.Race, TireCompound.Rally, TireCompound.Drag, TireCompound.Offroad];
    private static readonly SuspensionType[] SUS = [SuspensionType.Stock, SuspensionType.Street, SuspensionType.Sport, SuspensionType.Race, SuspensionType.Drift, SuspensionType.Offroad];
    private static readonly double[] WEIGHTS = [1800, 3300, 4600];
    private static readonly double[] POWERS = [90, 400, 1100];
    private static readonly double[] FWP = [42, 52, 62];

    // legal slider ranges (null = ride-height, checked against the part range separately)
    private static readonly Dictionary<string, (double lo, double hi)?> Limits = new()
    {
        ["tires.front"] = (15, 55), ["tires.rear"] = (15, 55), ["gearing.final"] = (2, 7),
        ["alignment.camberF"] = (-5, 0), ["alignment.camberR"] = (-5, 0), ["alignment.toeF"] = (-5, 5),
        ["alignment.toeR"] = (-5, 5), ["alignment.caster"] = (1, 7), ["arb.front"] = (1, 65), ["arb.rear"] = (1, 65),
        ["springs.rideF"] = null, ["springs.rideR"] = null, ["damping.reboundF"] = (1, 20), ["damping.reboundR"] = (1, 20),
        ["damping.bumpF"] = (1, 20), ["damping.bumpR"] = (1, 20), ["braking.balance"] = (40, 65), ["braking.pressure"] = (80, 130),
        ["differential.accel"] = (0, 100), ["differential.decel"] = (0, 100),
    };

    // The 27-config grid (DT × PT × EL), built with the SAME idx-derived assignments as sweep.js.
    public static IReadOnlyList<(string label, TuneInput input)> Configs { get; } = BuildConfigs();

    private static IReadOnlyList<(string, TuneInput)> BuildConfigs()
    {
        var list = new List<(string, TuneInput)>();
        int n = 0;
        foreach (var dt in DT)
            foreach (var pt in PT)
                foreach (var el in EL)
                {
                    int idx = n++;
                    var input = new TuneInput
                    {
                        Drivetrain = dt,
                        EngineLocation = el,
                        Powertrain = pt,
                        PiClass = PI[idx % 8],
                        Power = POWERS[idx % 3],
                        Torque = 200 + (idx % 5) * 90,
                        Weight = WEIGHTS[idx % 3],
                        FrontWeightPct = FWP[idx % 3],
                        Gears = new double[] { 4, 6, 8 }[idx % 3],
                        TireCompound = TC[idx % 7],
                        SuspensionType = SUS[idx % 6],
                        HasFrontAero = idx % 2 == 0,
                        HasRearAero = idx % 3 != 0,
                        AeroInstalled = true,
                        RideHeightMinF = 3.5, RideHeightMaxF = 6.5, RideHeightMinR = 3.5, RideHeightMaxR = 6.5,
                        SpringRateMinF = 200, SpringRateMaxF = 1100, SpringRateMinR = 200, SpringRateMaxR = 1100,
                        AeroFront = new AeroRange(30, 165), AeroRear = new AeroRange(50, 300),
                        RedlineRpm = 7000, TireDiameter = 26, TargetTopSpeed = null,
                        HandlingBias = 0, OverallStiffness = 0,
                    };
                    list.Add(($"{dt}/{pt}/{el}#{idx}", input));
                }
        return list;
    }

    public static TheoryData<int> ConfigIndices()
    {
        var data = new TheoryData<int>();
        for (int i = 0; i < Configs.Count; i++) data.Add(i);
        return data;
    }

    private static readonly double[] BiasSteps = BuildSteps();
    private static double[] BuildSteps()
    {
        var steps = new List<double>();
        for (double b = -5; b <= 5.0001; b += 0.5) steps.Add(JsMath.Round(b * 10) / 10);
        return steps.ToArray();
    }
    private static readonly (double bias, double stiff)[] Combos = [(-5, -5), (-5, 5), (5, -5), (5, 5)];

    private static JsonObject Tree(Tune t) => JsonSerializer.SerializeToNode(t, ParityHarness.RoundTrip)!.AsObject();

    private static string Strip(Tune t) => JsonSerializer.Serialize(new
    {
        tires = t.Tires, gearing = t.Gearing, alignment = t.Alignment, arb = t.Arb,
        springs = new { t.Springs.Front, t.Springs.Rear, t.Springs.RideF, t.Springs.RideR },
        damping = t.Damping, aero = new { t.Aero.Front, t.Aero.Rear },
        braking = t.Braking, differential = t.Differential,
    }, ParityHarness.RoundTrip);

    // Walk every numeric leaf; assert finite; assert ranged leaves in range; assert gears descending.
    private static void RangeCheck(Tune t, string ctx)
    {
        JsonObject tree = Tree(t);
        AssertAllFinite(tree, ctx);
        foreach (var (path, lim) in Limits)
        {
            if (lim is null) continue;
            JsonNode? node = GetPath(tree, path);
            if (node is null) continue;
            double v = node.GetValue<double>();
            Assert.True(v >= lim.Value.lo - 1e-9 && v <= lim.Value.hi + 1e-9, $"{path}={v} out of [{lim.Value.lo},{lim.Value.hi}] @ {ctx}");
        }
        foreach (var path in new[] { "springs.rideF", "springs.rideR" })
        {
            double v = GetPath(tree, path)!.GetValue<double>();
            Assert.True(v >= 3.5 - 1e-9 && v <= 6.5 + 1e-9, $"{path}={v} out of part range @ {ctx}");
        }
        if (!t.Gearing.SingleSpeed)
            for (int k = 1; k < t.Gearing.Ratios.Count; k++)
                Assert.True(t.Gearing.Ratios[k] < t.Gearing.Ratios[k - 1], $"gears not descending @ {ctx}");
    }

    private static void AssertAllFinite(JsonNode? node, string ctx)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var kv in o) AssertAllFinite(kv.Value, ctx);
                break;
            case JsonArray a:
                foreach (var el in a) AssertAllFinite(el, ctx);
                break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number:
                double d = v.GetValue<double>();
                Assert.True(!double.IsNaN(d) && !double.IsInfinity(d), $"NaN/Inf leaf @ {ctx}");
                break;
        }
    }

    private static JsonNode? GetPath(JsonObject root, string path)
    {
        JsonNode? cur = root;
        foreach (var key in path.Split('.'))
        {
            if (cur is JsonObject o && o.TryGetPropertyValue(key, out JsonNode? next)) cur = next;
            else return null;
        }
        return cur;
    }

    /// <summary>
    /// Per-config sweep: across the full bias range, the full stiffness range, and both-extreme
    /// combos, every output is finite and in range, gears descend; and bias-0 / stiff-0 reproduce the
    /// baseline byte-for-byte. One theory row per config keeps reporting granular.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConfigIndices))]
    public void ConfigSweep_RangeNeutralityAndDescent(int cfgIdx)
    {
        var (label, cfg) = Configs[cfgIdx];
        foreach (Goal g in Engine.Goals)
        {
            Tune baseline = Engine.Compute(cfg, g);
            string baselineStr = Strip(baseline);

            // bias-0 / stiff-0 byte-for-byte neutrality
            string biasZero = Strip(Engine.Compute(cfg with { HandlingBias = 0 }, g));
            Assert.Equal(baselineStr, biasZero);
            string stiffZero = Strip(Engine.Compute(cfg with { OverallStiffness = 0 }, g));
            Assert.Equal(baselineStr, stiffZero);

            foreach (double b in BiasSteps)
                RangeCheck(Engine.Compute(cfg with { HandlingBias = b }, g), $"{label}/{g}/bias{b}");
            foreach (double s in BiasSteps)
                RangeCheck(Engine.Compute(cfg with { OverallStiffness = s }, g), $"{label}/{g}/stiff{s}");
            foreach (var (b, s) in Combos)
                RangeCheck(Engine.Compute(cfg with { HandlingBias = b, OverallStiffness = s }, g), $"{label}/{g}/bias{b}+stiff{s}");
        }
    }

    [Fact]
    public void EachConfig_SixGoalsMutuallyDistinct()
    {
        foreach (var (label, cfg) in Configs)
        {
            var perGoal = Engine.Goals.Select(g => JsonSerializer.Serialize(Tree(Engine.Compute(cfg, g)))).ToHashSet();
            Assert.True(perGoal.Count == Engine.Goals.Count, $"goals not all distinct for {label}");
        }
    }

    [Fact]
    public void VaryingOnlyDrivetrain_ProducesDistinctTunes()
    {
        foreach (var (label, cfg) in Configs)
        {
            var tunes = DT.Select(dt => JsonSerializer.Serialize(Tree(Engine.Compute(cfg with { Drivetrain = dt }, Goal.Circuit)))).ToHashSet();
            Assert.True(tunes.Count == DT.Length, $"drivetrain not distinct for {label}");
        }
    }
}
