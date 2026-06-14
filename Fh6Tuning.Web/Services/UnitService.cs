using System.Globalization;
using Fh6Tuning.Core;

namespace Fh6Tuning.Web.Services;

/// <summary>
/// Port of the unit-handling and display-formatting helpers from legacy/app.js (lines 11-55,
/// 504-531). The engine is imperial-only; this service converts metric input → imperial before
/// <c>compute</c> and imperial → metric for display, and reproduces the JS number formatters
/// (<c>nf</c>, <c>springDisp</c>, <c>rideDisp</c>, <c>aeroDisp</c>, <c>speedDisp</c>) byte-for-byte.
///
/// All math is <see cref="double"/> using <see cref="CultureInfo.InvariantCulture"/> so the
/// '.' decimal separator is fixed regardless of host locale — exactly like the JS engine.
/// </summary>
public sealed class UnitService
{
    /// <summary>A unit-bound physical dimension. The legacy <c>M2I</c> / <c>FIELD_DIM</c> keys.</summary>
    public enum Dim { Weight, Torque, Ride, Spring, Aero, Speed }

    // factor to convert a METRIC value to IMPERIAL (multiply); divide to go back.
    // aero force: 1 kgf = 2.2046226 lbf (same numeric factor as mass). (legacy/app.js:14)
    private static readonly IReadOnlyDictionary<Dim, double> M2I = new Dictionary<Dim, double>
    {
        [Dim.Weight] = 2.2046226,
        [Dim.Torque] = 0.7375621,
        [Dim.Ride] = 0.3937008,
        [Dim.Spring] = 55.99741,
        [Dim.Aero] = 2.2046226,
        [Dim.Speed] = 0.6213712,
    };

    // legacy UNIT_LABEL (app.js:15-18). Indexed by [metric?][dim-label].
    private static readonly IReadOnlyDictionary<Dim, string> ImperialLabel = new Dictionary<Dim, string>
    {
        [Dim.Weight] = "(lb)",
        [Dim.Torque] = "(lb-ft)",
        [Dim.Ride] = "(in)",
        [Dim.Spring] = "(lb/in)",
        [Dim.Aero] = "(lbf)",
        [Dim.Speed] = "(mph)",
    };

    private static readonly IReadOnlyDictionary<Dim, string> MetricLabel = new Dictionary<Dim, string>
    {
        [Dim.Weight] = "(kg)",
        [Dim.Torque] = "(Nm)",
        [Dim.Ride] = "(cm)",
        [Dim.Spring] = "(kgf/mm)",
        [Dim.Aero] = "(kgf)",
        [Dim.Speed] = "(km/h)",
    };

    private const string PowerLabel = "(hp)"; // power has no metric conversion (legacy: same in both)

    // legacy FIELD_DIM (app.js:19-27): which form fields are unit-bound + their dimension. Tire
    // width/aspect/rim are deliberately ABSENT so the metric toggle never rewrites them.
    public static readonly IReadOnlyDictionary<string, Dim> FieldDim = new Dictionary<string, Dim>
    {
        ["weight"] = Dim.Weight,
        ["torque"] = Dim.Torque,
        ["rideHeightMinF"] = Dim.Ride,
        ["rideHeightMaxF"] = Dim.Ride,
        ["rideHeightMinR"] = Dim.Ride,
        ["rideHeightMaxR"] = Dim.Ride,
        ["springRateMinF"] = Dim.Spring,
        ["springRateMaxF"] = Dim.Spring,
        ["springRateMinR"] = Dim.Spring,
        ["springRateMaxR"] = Dim.Spring,
        ["aeroFrontMin"] = Dim.Aero,
        ["aeroFrontMax"] = Dim.Aero,
        ["aeroRearMin"] = Dim.Aero,
        ["aeroRearMax"] = Dim.Aero,
        ["targetTopSpeed"] = Dim.Speed,
    };

    /// <summary>legacy <c>toImp(dim, v)</c> — metric → imperial (×factor), else identity.</summary>
    public double ToImp(Dim dim, double v, UnitSystem units) =>
        units == UnitSystem.Metric ? v * M2I[dim] : v;

    /// <summary>legacy <c>fromImp(dim, v)</c> — imperial → metric (÷factor), else identity.</summary>
    public double FromImp(Dim dim, double v, UnitSystem units) =>
        units == UnitSystem.Metric ? v / M2I[dim] : v;

    /// <summary>The raw metric-to-imperial factor for a dimension (legacy M2I lookup).</summary>
    public double Factor(Dim dim) => M2I[dim];

    /// <summary>The unit label suffix shown next to a field, e.g. "(lb)" / "(kg)".</summary>
    public string Label(Dim dim, UnitSystem units) =>
        (units == UnitSystem.Metric ? MetricLabel : ImperialLabel)[dim];

    /// <summary>Power label — "(hp)" in both unit systems (legacy UNIT_LABEL.power).</summary>
    public string PowerUnitLabel => PowerLabel;

    // Bare (no-parens) display suffixes shared by the cards, the compare table and the copy-to-text
    // block, so a label change lands in one place and the three surfaces can't diverge.
    /// <summary>Spring-rate suffix: "kgf/mm" (metric) / "lb/in" (imperial).</summary>
    public static string SpringUnit(UnitSystem units) => units == UnitSystem.Metric ? "kgf/mm" : "lb/in";
    /// <summary>Ride-height suffix: "cm" (metric) / "in" (imperial).</summary>
    public static string RideUnit(UnitSystem units) => units == UnitSystem.Metric ? "cm" : "in";
    /// <summary>Downforce-force suffix: "kgf" (metric) / "lbf" (imperial).</summary>
    public static string AeroUnit(UnitSystem units) => units == UnitSystem.Metric ? "kgf" : "lbf";
    /// <summary>Speed suffix: "km/h" (metric) / "mph" (imperial).</summary>
    public static string SpeedUnit(UnitSystem units) => units == UnitSystem.Metric ? "km/h" : "mph";

    /// <summary>
    /// legacy <c>nf(v, dp = 1)</c>: pretty number — "—" for null/NaN, else <c>toFixed(dp)</c>
    /// with trailing zeros (and a bare trailing dot) trimmed.
    /// Ports the two JS regexes: <c>/\.0+$/</c> → "" and <c>/(\.\d*?)0+$/</c> → "$1".
    /// </summary>
    public string Nf(double? v, int dp = 1)
    {
        if (v is null || double.IsNaN(v.Value)) return "—";
        return FormatTrimmed(v.Value, dp);
    }

    /// <summary>JS <c>Number(v).toFixed(dp)</c> + trailing-zero trim, no null/NaN guard.</summary>
    private static string FormatTrimmed(double v, int dp)
    {
        // JS toFixed rounds half away from zero for the values the formatters see; .NET
        // "F" uses MidpointRounding.ToEven by default but, like JS toFixed, operates on the
        // already-engine-quantized double — both produce the same fixed-dp text here. Display
        // formatters are NOT part of the numeric-parity contract (that lives in Core/JsMath).
        string s = v.ToString("F" + dp.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (!s.Contains('.')) return s;
        // /\.0+$/ → ""  (e.g. "4.00" -> "4", "4.0" -> "4")
        // /(\.\d*?)0+$/ → "$1"  (e.g. "4.50" -> "4.5", "4.530" -> "4.53")
        int end = s.Length;
        while (end > 0 && s[end - 1] == '0') end--;
        if (end > 0 && s[end - 1] == '.') end--; // drop a now-bare trailing dot
        return s[..end];
    }

    /// <summary>legacy <c>springDisp(vImp)</c> — spring rate, 2 dp in metric, 0 dp in imperial.</summary>
    public string SpringDisp(double vImp, UnitSystem units) =>
        Nf(FromImp(Dim.Spring, vImp, units), units == UnitSystem.Metric ? 2 : 0);

    /// <summary>legacy <c>rideDisp(vImp)</c> — ride height, always 1 dp.</summary>
    public string RideDisp(double vImp, UnitSystem units) =>
        Nf(FromImp(Dim.Ride, vImp, units), 1);

    /// <summary>legacy <c>aeroDisp(vImp)</c> — downforce, 0 dp + " kgf"/" lbf" suffix.</summary>
    public string AeroDisp(double vImp, UnitSystem units) =>
        Nf(FromImp(Dim.Aero, vImp, units), 0) + " " + AeroUnit(units);

    /// <summary>legacy <c>speedDisp(mphImp)</c> — speed, 0 dp + " km/h"/" mph" suffix.</summary>
    public string SpeedDisp(double mphImp, UnitSystem units) =>
        Nf(FromImp(Dim.Speed, mphImp, units), 0) + " " + SpeedUnit(units);
}
