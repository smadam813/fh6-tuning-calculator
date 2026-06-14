namespace Fh6Tuning.Core;

/// <summary>
/// The rounding / parity core. Mirrors <c>legacy/tuning.js</c> lines 40-46 exactly.
///
/// JS <c>Math.round</c> rounds half <b>toward +Infinity</b>
/// (<c>round(0.5)=1</c>, <c>round(2.5)=3</c>, <c>round(-0.5)=0</c>, <c>round(-2.5)=-2</c>).
/// C# <c>Math.Round</c> defaults to banker's rounding — <b>NEVER use <c>Math.Round</c> in Core.</b>
///
/// All math is <see cref="double"/> (IEEE-754, identical to JS <c>Number</c>);
/// <b>never <c>decimal</c>, never <c>float</c>.</b>
/// </summary>
public static class JsMath
{
    /// <summary>JS <c>Math.round</c>: half rounds toward +Infinity. <c>Math.floor(x + 0.5)</c>.</summary>
    public static double Round(double x) => Math.Floor(x + 0.5);

    /// <summary>legacy <c>clamp(x, lo, hi) = Math.min(hi, Math.max(lo, x))</c>.</summary>
    public static double Clamp(double x, double lo, double hi) => Math.Min(hi, Math.Max(lo, x));

    /// <summary>legacy <c>r1</c>: round to 1 decimal — <c>Math.round(x*10)/10</c>.</summary>
    public static double R1(double x) => Round(x * 10) / 10;

    /// <summary>legacy <c>r2</c>: round to 2 decimals — <c>Math.round(x*100)/100</c>.</summary>
    public static double R2(double x) => Round(x * 100) / 100;

    /// <summary>legacy <c>rHalf</c>: round to nearest 0.5 — <c>Math.round(x*2)/2</c>.</summary>
    public static double RHalf(double x) => Round(x * 2) / 2;

    /// <summary>legacy <c>r5</c>: round to nearest 5 — <c>Math.round(x/5)*5</c>.</summary>
    public static double R5(double x) => Round(x / 5) * 5;

    /// <summary>legacy <c>rInt</c>: round to integer — <c>Math.round(x)</c>.</summary>
    public static double RInt(double x) => Round(x);

    /// <summary>legacy <c>rEven</c>: round to nearest even — <c>Math.round(x/2)*2</c>.</summary>
    public static double REven(double x) => Round(x / 2) * 2;

    /// <summary>
    /// <c>JSON.stringify(-0) === "0"</c> in JS, but <c>System.Text.Json</c> writes <c>"-0"</c>.
    /// Apply to every signed rounded output at the point it is assigned to a record.
    /// No-op for any non-negative-zero value (<c>-0.0 == 0.0</c> is true, so this returns <c>0.0</c>).
    /// </summary>
    public static double NormZero(double x) => x == 0.0 ? 0.0 : x;
}
