using System.Text.Json.Serialization;

namespace Fh6Tuning.Core;

/// <summary>
/// The nullable <c>[min, max]</c> lbf downforce pair. In JS <c>aeroFront</c>/<c>aeroRear</c> are
/// <c>[min, max]</c> arrays where either or both elements may be <c>null</c>
/// (<c>[null, null]</c> default); <c>hasRange</c> requires both present
/// (legacy/tuning.js:458-460). Modeled as a <c>record struct</c> of two <see cref="double"/>?.
/// Serializes as a 2-element JSON array (see <see cref="Serialization.AeroRangeJsonConverter"/>).
/// </summary>
[JsonConverter(typeof(Serialization.AeroRangeJsonConverter))]
public readonly record struct AeroRange(double? Min, double? Max)
{
    public static readonly AeroRange None = new(null, null);

    /// <summary>legacy <c>hasRange</c>: <c>rng[0] != null &amp;&amp; rng[1] != null</c>.</summary>
    public bool HasRange => Min is not null && Max is not null;

    /// <summary>
    /// legacy <c>toLbf(frac, rng)</c>: <c>Math.round(min + (max-min)*clamp(frac,0,1))</c>, or null.
    /// </summary>
    public double? ToLbf(double frac) =>
        HasRange
            ? JsMath.Round(Min!.Value + (Max!.Value - Min!.Value) * JsMath.Clamp(frac, 0, 1))
            : null;
}
