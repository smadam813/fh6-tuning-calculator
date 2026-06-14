using Fh6Tuning.Core;
using Fh6Tuning.Web.Services;

namespace Fh6Tuning.Web.Tests;

/// <summary>
/// Coverage for <see cref="UnitService"/> — the documented correctness-critical metric↔imperial seam
/// (CLAUDE.md) and the JS number formatters it ports (nf / springDisp / rideDisp / aeroDisp /
/// speedDisp). The legacy app.js had no tests here; the port turned this conversion math into a pure,
/// directly-testable service, so these lock down the round-trip identity, the deliberate tire-dimension
/// exclusion from the unit toggle, and the trailing-zero-trim formatting.
/// </summary>
public sealed class UnitServiceTests
{
    private readonly UnitService _u = new();

    public static TheoryData<UnitService.Dim> AllDims()
    {
        var d = new TheoryData<UnitService.Dim>();
        foreach (UnitService.Dim dim in Enum.GetValues<UnitService.Dim>()) d.Add(dim);
        return d;
    }

    // ---- ToImp / FromImp round-trip identity ----

    [Theory]
    [MemberData(nameof(AllDims))]
    public void MetricRoundTrip_IsIdentity_WithinFpTolerance(UnitService.Dim dim)
    {
        // A metric value → imperial → metric must return the original (no lossy intermediate rounding
        // in the service itself; the only rounding is in the display formatters / SetUnits).
        foreach (double metric in new[] { 1.0, 42.0, 3300.0, 0.5, 123.456 })
        {
            double imp = _u.ToImp(dim, metric, UnitSystem.Metric);
            double back = _u.FromImp(dim, imp, UnitSystem.Metric);
            Assert.Equal(metric, back, 9);
        }
    }

    [Theory]
    [MemberData(nameof(AllDims))]
    public void ImperialIsAnIdentity_NoConversion(UnitService.Dim dim)
    {
        // In imperial mode the engine's native units pass straight through (factor never applied).
        Assert.Equal(2750.0, _u.ToImp(dim, 2750.0, UnitSystem.Imperial));
        Assert.Equal(2750.0, _u.FromImp(dim, 2750.0, UnitSystem.Imperial));
    }

    [Fact]
    public void KnownConversions_MatchLegacyFactors()
    {
        // 1 kg → 2.2046226 lb; 1 in (from 0.3937008 factor) etc. — the legacy M2I constants.
        Assert.Equal(2.2046226, _u.ToImp(UnitService.Dim.Weight, 1.0, UnitSystem.Metric), 6);
        Assert.Equal(0.7375621, _u.ToImp(UnitService.Dim.Torque, 1.0, UnitSystem.Metric), 6);
        Assert.Equal(55.99741, _u.ToImp(UnitService.Dim.Spring, 1.0, UnitSystem.Metric), 5);
        Assert.Equal(2.2046226, _u.ToImp(UnitService.Dim.Aero, 1.0, UnitSystem.Metric), 6);
        Assert.Equal(0.6213712, _u.ToImp(UnitService.Dim.Speed, 1.0, UnitSystem.Metric), 6);
        // ride: 1 cm = 0.3937008 in
        Assert.Equal(0.3937008, _u.ToImp(UnitService.Dim.Ride, 1.0, UnitSystem.Metric), 6);
    }

    // ---- the deliberate tire width/aspect/rim exclusion from the unit toggle ----

    [Fact]
    public void FieldDim_ExcludesTireDimensions_SoTheTogglerNeverRewritesThem()
    {
        // CLAUDE.md: tire width/aspect/rim are unit-independent in Forza and are kept OUT of FIELD_DIM
        // so the metric toggle never converts them. Guard that exclusion.
        Assert.DoesNotContain("tireWidth", UnitService.FieldDim.Keys);
        Assert.DoesNotContain("tireAspect", UnitService.FieldDim.Keys);
        Assert.DoesNotContain("tireRim", UnitService.FieldDim.Keys);
        // power has no metric conversion either (same label/value in both systems).
        Assert.DoesNotContain("power", UnitService.FieldDim.Keys);
    }

    [Fact]
    public void FieldDim_CoversTheUnitBoundFields()
    {
        // The fields that DO convert (legacy FIELD_DIM) — weight, torque, the spring/ride/aero ranges
        // and target top speed.
        Assert.Equal(UnitService.Dim.Weight, UnitService.FieldDim["weight"]);
        Assert.Equal(UnitService.Dim.Torque, UnitService.FieldDim["torque"]);
        Assert.Equal(UnitService.Dim.Spring, UnitService.FieldDim["springRateMinF"]);
        Assert.Equal(UnitService.Dim.Ride, UnitService.FieldDim["rideHeightMaxR"]);
        Assert.Equal(UnitService.Dim.Aero, UnitService.FieldDim["aeroRearMax"]);
        Assert.Equal(UnitService.Dim.Speed, UnitService.FieldDim["targetTopSpeed"]);
    }

    // ---- labels ----

    [Fact]
    public void Labels_FlipWithUnitSystem()
    {
        Assert.Equal("(lb)", _u.Label(UnitService.Dim.Weight, UnitSystem.Imperial));
        Assert.Equal("(kg)", _u.Label(UnitService.Dim.Weight, UnitSystem.Metric));
        Assert.Equal("(lb/in)", _u.Label(UnitService.Dim.Spring, UnitSystem.Imperial));
        Assert.Equal("(kgf/mm)", _u.Label(UnitService.Dim.Spring, UnitSystem.Metric));
        Assert.Equal("(mph)", _u.Label(UnitService.Dim.Speed, UnitSystem.Imperial));
        Assert.Equal("(km/h)", _u.Label(UnitService.Dim.Speed, UnitSystem.Metric));
        Assert.Equal("(hp)", _u.PowerUnitLabel); // power: same in both
    }

    // ---- nf / display formatters (trailing-zero trim) ----

    [Theory]
    [InlineData(null, 1, "—")]
    [InlineData(4.0, 1, "4")]       // /\.0+$/ → ""
    [InlineData(4.5, 1, "4.5")]
    [InlineData(4.50, 2, "4.5")]    // /(\.\d*?)0+$/ → "$1"
    [InlineData(4.53, 2, "4.53")]
    [InlineData(4.530, 3, "4.53")]
    [InlineData(0.0, 2, "0")]
    public void Nf_TrimsTrailingZeros_LikeLegacy(double? v, int dp, string expected)
    {
        Assert.Equal(expected, _u.Nf(v, dp));
    }

    [Fact]
    public void Nf_NaN_RendersDash()
    {
        Assert.Equal("—", _u.Nf(double.NaN, 1));
    }

    [Fact]
    public void SpringDisp_2dpMetric_0dpImperial()
    {
        // imperial: 0 dp, value passes through; metric: 2 dp, ÷ spring factor.
        Assert.Equal("400", _u.SpringDisp(400, UnitSystem.Imperial));
        Assert.Equal("7.14", _u.SpringDisp(400, UnitSystem.Metric)); // 400 / 55.99741 = 7.143...
    }

    [Fact]
    public void RideDisp_Always1dp()
    {
        Assert.Equal("5", _u.RideDisp(5.0, UnitSystem.Imperial));    // 5.0 → "5"
        Assert.Equal("12.7", _u.RideDisp(5.0, UnitSystem.Metric));   // 5 in = 12.7 cm
    }

    [Fact]
    public void AeroDisp_AppendsUnit()
    {
        Assert.Equal("165 lbf", _u.AeroDisp(165, UnitSystem.Imperial));
        Assert.EndsWith(" kgf", _u.AeroDisp(165, UnitSystem.Metric));
    }

    [Fact]
    public void SpeedDisp_AppendsUnit()
    {
        Assert.Equal("180 mph", _u.SpeedDisp(180, UnitSystem.Imperial));
        Assert.EndsWith(" km/h", _u.SpeedDisp(180, UnitSystem.Metric));
        // 180 mph ÷ 0.6213712 ≈ 290 km/h
        Assert.Equal("290 km/h", _u.SpeedDisp(180, UnitSystem.Metric));
    }
}
