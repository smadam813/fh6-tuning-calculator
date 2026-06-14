using Fh6Tuning.Core;
using Fh6Tuning.Web.Services;

namespace Fh6Tuning.Web.Tests;

/// <summary>
/// Coverage for <see cref="TuneFormatter"/> — the formerly DOM-bound app.js display layer (card rows,
/// the compare table, the "what the sliders changed" diff, and the copy-to-text block), now a pure
/// service. Tunes are produced by the real engine so the row labels / unit suffixes / number
/// formatting are checked against actual outputs, not hand-mocked shapes.
/// </summary>
public sealed class TuneFormatterTests
{
    private static readonly ITuningEngine Engine = new TuningEngine();
    private readonly UnitService _u = new();
    private readonly TuneFormatter _f;

    public TuneFormatterTests() => _f = new TuneFormatter(_u);

    // A simple, fully-specified RWD ICE car (mirrors the kind of input CalculatorState builds).
    private static TuneInput RwdCar(Action<TuneInputBuilder>? tweak = null)
    {
        var b = new TuneInputBuilder();
        tweak?.Invoke(b);
        return b.Build();
    }

    // Small mutable builder so each test can adjust just what it needs.
    private sealed class TuneInputBuilder
    {
        public Drivetrain Drivetrain = Drivetrain.RWD;
        public EngineLocation EngineLocation = EngineLocation.Front;
        public Powertrain Powertrain = Powertrain.ICE;
        public PiClass PiClass = PiClass.A;
        public TireCompound TireCompound = TireCompound.Sport;
        public SuspensionType SuspensionType = SuspensionType.Race;
        public double Power = 400, Torque = 370, Weight = 3300, FrontWeightPct = 52, Gears = 6;
        public bool HasFrontAero = true, HasRearAero = true, AeroInstalled = true;
        public double? RedlineRpm, TireDiameter;
        public AeroRange AeroFront = AeroRange.None, AeroRear = AeroRange.None;

        public TuneInput Build() => new()
        {
            Drivetrain = Drivetrain, EngineLocation = EngineLocation, Powertrain = Powertrain,
            PiClass = PiClass, TireCompound = TireCompound, SuspensionType = SuspensionType,
            Power = Power, Torque = Torque, Weight = Weight, FrontWeightPct = FrontWeightPct, Gears = Gears,
            HasFrontAero = HasFrontAero, HasRearAero = HasRearAero, AeroInstalled = AeroInstalled,
            RideHeightMinF = 4.5, RideHeightMaxF = 7.0, RideHeightMinR = 4.5, RideHeightMaxR = 7.0,
            SpringRateMinF = 150, SpringRateMaxF = 900, SpringRateMinR = 150, SpringRateMaxR = 900,
            AeroFront = AeroFront, AeroRear = AeroRear,
            RedlineRpm = RedlineRpm, TireDiameter = TireDiameter,
            HandlingBias = 0, OverallStiffness = 0,
        };
    }

    // ---- card rows ----

    [Fact]
    public void Cards_AreTheNineLegacyCardsInOrder()
    {
        var ids = _f.Cards(UnitSystem.Imperial).Select(c => c.Id).ToArray();
        Assert.Equal(
            new[] { "tires", "gearing", "alignment", "arb", "springs", "damping", "aero", "braking", "differential" },
            ids);
    }

    [Fact]
    public void TiresCard_RowsHavePsiSuffix()
    {
        var tune = Engine.Compute(RwdCar(), Goal.Circuit);
        var tires = _f.Cards(UnitSystem.Imperial).First(c => c.Id == "tires");
        var rows = tires.Rows(tune);
        Assert.Equal("Front pressure", rows[0].Key);
        Assert.EndsWith(" psi", rows[0].Value);
        Assert.Equal("Rear pressure", rows[1].Key);
    }

    [Fact]
    public void SpringsCard_UnitSuffix_FlipsWithUnitSystem()
    {
        var tune = Engine.Compute(RwdCar(), Goal.Circuit);
        var imp = _f.Cards(UnitSystem.Imperial).First(c => c.Id == "springs").Rows(tune);
        var met = _f.Cards(UnitSystem.Metric).First(c => c.Id == "springs").Rows(tune);
        Assert.EndsWith(" lb/in", imp[0].Value);
        Assert.EndsWith(" kgf/mm", met[0].Value);
        Assert.EndsWith(" in", imp[2].Value);   // ride height F
        Assert.EndsWith(" cm", met[2].Value);
    }

    [Fact]
    public void DiffRows_Rwd_TwoRows_PrefixedWithDriveline()
    {
        var tune = Engine.Compute(RwdCar(), Goal.Circuit);
        var rows = _f.DiffRows(tune);
        Assert.Equal(2, rows.Count);
        Assert.Equal("RWD accel", rows[0].Key);
        Assert.Equal("RWD decel", rows[1].Key);
        Assert.EndsWith("%", rows[0].Value);
    }

    [Fact]
    public void DiffRows_Awd_FiveRows_IncludingCenter()
    {
        var tune = Engine.Compute(RwdCar(b => b.Drivetrain = Drivetrain.AWD), Goal.Circuit);
        var rows = _f.DiffRows(tune);
        Assert.Equal(5, rows.Count);
        Assert.Equal("Front accel", rows[0].Key);
        Assert.Equal("Center (→rear)", rows[4].Key);
    }

    [Fact]
    public void GearRows_Ev_ShowsLoneFirstGear_NotSixGears()
    {
        // EV → single-speed: one "1st (only gear)" sub-row, no Gear 2..N rows.
        var tune = Engine.Compute(RwdCar(b => b.Powertrain = Powertrain.EV), Goal.Circuit);
        Assert.True(tune.Gearing.SingleSpeed);
        var rows = _f.GearRows(tune, UnitSystem.Imperial);
        Assert.Contains(rows, r => r.Key == "1st (only gear)");
        Assert.DoesNotContain(rows, r => r.Key == "Gear 2");
    }

    [Fact]
    public void AeroVal_NoRange_ShowsPercentOfRange()
    {
        // % given, no lbf → "{pct}% of range".
        Assert.Equal("85% of range", _f.AeroVal(85, null, "—", UnitSystem.Imperial));
        // null pct → absent label.
        Assert.Equal("— (no wing)", _f.AeroVal(null, null, "— (no wing)", UnitSystem.Imperial));
        // lbf given → "{aeroDisp} ({pct}%)".
        Assert.Equal("120 lbf (85%)", _f.AeroVal(85, 120, "—", UnitSystem.Imperial));
    }

    // ---- compare table ----

    [Fact]
    public void CompareRowDefs_Ev_SuppressesPerGearRows()
    {
        var input = RwdCar(b => b.Powertrain = Powertrain.EV);
        var defs = _f.CompareRowDefs(input, UnitSystem.Imperial);
        Assert.DoesNotContain(defs, d => d.Label == "Gear 1");
        Assert.Contains(defs, d => d.Label == "Final drive");
    }

    [Fact]
    public void CompareRowDefs_NonEv_HasPerGearRows()
    {
        var input = RwdCar();
        var defs = _f.CompareRowDefs(input, UnitSystem.Imperial);
        Assert.Contains(defs, d => d.Label == "Gear 1");
        Assert.Contains(defs, d => d.Label == "Gear 6");
        Assert.DoesNotContain(defs, d => d.Label == "Gear 7");
    }

    [Fact]
    public void CompareRowDefs_Awd_HasFiveDiffRows()
    {
        var input = RwdCar(b => b.Drivetrain = Drivetrain.AWD);
        var defs = _f.CompareRowDefs(input, UnitSystem.Imperial);
        Assert.Contains(defs, d => d.Label == "Front accel");
        Assert.Contains(defs, d => d.Label == "Center→rear");
    }

    [Fact]
    public void CompareRowDefs_TopSpeedRow_OnlyWithRedlineAndTireDiameter()
    {
        var without = _f.CompareRowDefs(RwdCar(), UnitSystem.Imperial);
        Assert.DoesNotContain(without, d => d.Label is not null && d.Label.StartsWith("Top speed"));

        var with = _f.CompareRowDefs(
            RwdCar(b => { b.RedlineRpm = 7000; b.TireDiameter = 26; }), UnitSystem.Imperial);
        Assert.Contains(with, d => d.Label is not null && d.Label.StartsWith("Top speed"));
    }

    // ---- "what the sliders changed" diff ----

    [Fact]
    public void CollectChanges_NoDialMovement_NoChanges()
    {
        var input = RwdCar();
        var baseline = Engine.Compute(input, Goal.Circuit);
        var live = Engine.Compute(input, Goal.Circuit); // identical
        Assert.Empty(_f.CollectChanges(live, baseline, UnitSystem.Imperial));
    }

    [Fact]
    public void CollectChanges_StiffnessUp_ProducesStifferSpringEffects()
    {
        var input = RwdCar();
        var baseline = Engine.Compute(input, Goal.Circuit);
        var live = Engine.Compute(input with { OverallStiffness = 5 }, Goal.Circuit);
        var changes = _f.CollectChanges(live, baseline, UnitSystem.Imperial);
        Assert.NotEmpty(changes);
        // stiffening raises spring rate → a "stiffer ... springs" effect should appear, marked up.
        Assert.Contains(changes, c => c.Text.Contains("stiffer") && c.Text.Contains("springs"));
        Assert.Contains(changes, c => c.Up);
    }

    [Fact]
    public void EffectPhrase_Springs_RateVsRideHeight()
    {
        var stiffer = _f.EffectPhrase("springs", "Front rate", "300", "360");
        Assert.Equal("stiffer front springs", stiffer.Text);
        Assert.True(stiffer.Up);

        var lower = _f.EffectPhrase("springs", "Ride height F", "5.0", "4.5");
        Assert.Equal("lower front ride height", lower.Text);
        Assert.False(lower.Up);
    }

    [Fact]
    public void EffectPhrase_Braking_BiasDirection()
    {
        Assert.Equal("more front brake bias", _f.EffectPhrase("braking", "Balance (→front)", "52", "55").Text);
        Assert.Equal("more rear brake bias", _f.EffectPhrase("braking", "Balance (→front)", "55", "52").Text);
    }

    // ---- copy-to-text ----

    [Fact]
    public void TuneToText_ContainsHeaderAndAllSections()
    {
        var input = RwdCar();
        var tune = Engine.Compute(input, Goal.Circuit);
        var meta = Engine.GoalMeta[Goal.Circuit];
        string text = _f.TuneToText(tune, input, meta, UnitSystem.Imperial);

        Assert.StartsWith("FH6 TUNE — Circuit", text);
        Assert.Contains("— Tires:", text);
        Assert.Contains("— Final drive:", text);
        Assert.Contains("— ARB:", text);
        Assert.Contains("— Brakes:", text);
        Assert.Contains("— Diff: RWD accel", text); // RWD single-line diff
    }

    [Fact]
    public void TuneToText_Awd_DiffLineIsThreeWay()
    {
        var input = RwdCar(b => b.Drivetrain = Drivetrain.AWD);
        var tune = Engine.Compute(input, Goal.Circuit);
        var meta = Engine.GoalMeta[Goal.Circuit];
        string text = _f.TuneToText(tune, input, meta, UnitSystem.Imperial);
        Assert.Contains("— Diff: front", text);
        Assert.Contains("center", text);
    }

    [Fact]
    public void TuneToText_DialsLine_OnlyWhenADialIsOffCenter()
    {
        var input = RwdCar();
        var meta = Engine.GoalMeta[Goal.Circuit];

        string none = _f.TuneToText(Engine.Compute(input, Goal.Circuit), input, meta, UnitSystem.Imperial);
        Assert.DoesNotContain("Dials:", none);

        var dialed = input with { HandlingBias = 5 };
        string with = _f.TuneToText(Engine.Compute(dialed, Goal.Circuit), dialed, meta, UnitSystem.Imperial);
        Assert.Contains("Dials:", with);
        Assert.Contains("oversteer", with);
    }
}
