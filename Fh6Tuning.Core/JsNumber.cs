using System.Globalization;

namespace Fh6Tuning.Core;

/// <summary>
/// JS <c>Number()</c> / <c>parseFloat</c> coercion. <c>validate()</c> and the Web parse layer both
/// depend on JS number coercion semantics; centralized here so they match exactly.
/// </summary>
public static class JsNumber
{
    /// <summary>
    /// Mirrors JS <c>Number(v)</c> on the values <c>validate()</c> actually sees:
    /// <list type="bullet">
    ///   <item>null/undefined (Absent) → NaN</item>
    ///   <item>"" / whitespace-only → 0 (JS <c>Number("")===0</c>, <c>Number("  ")===0</c>)</item>
    ///   <item>numeric string → that number ("1e3"→1000, " 12 "→12)</item>
    ///   <item>non-numeric string → NaN ("heavy"→NaN)</item>
    ///   <item>a real double → itself (NaN/±Infinity pass through unchanged)</item>
    ///   <item>bool → 1 / 0 (JS <c>Number(true)===1</c>)</item>
    /// </list>
    /// </summary>
    public static double Coerce(RawValue v) => v.Kind switch
    {
        RawKind.Absent => double.NaN,
        RawKind.Num => v.AsNumber,
        RawKind.Bool => v.AsBool ? 1.0 : 0.0,
        RawKind.Str => CoerceString(v.AsString!),
        _ => double.NaN,
    };

    /// <summary>JS <c>Number(string)</c>: full-string numeric parse, whitespace → 0, else NaN.</summary>
    private static double CoerceString(string s)
    {
        // JS Number() trims leading/trailing whitespace; an empty/whitespace-only string is 0.
        string t = s.Trim();
        if (t.Length == 0) return 0.0;

        // JS literals Number() accepts beyond plain decimals.
        switch (t)
        {
            case "Infinity":
            case "+Infinity":
                return double.PositiveInfinity;
            case "-Infinity":
                return double.NegativeInfinity;
        }

        // Hex / octal / binary integer literals (JS Number accepts these, parseFloat does not).
        if (t.Length > 2 && (t[0] == '0'))
        {
            char p = char.ToLowerInvariant(t[1]);
            int radixBase = p switch { 'x' => 16, 'o' => 8, 'b' => 2, _ => 0 };
            if (radixBase != 0)
            {
                try
                {
                    return Convert.ToInt64(t[2..], radixBase);
                }
                catch
                {
                    return double.NaN;
                }
            }
        }

        // Plain decimal / exponential. The ENTIRE trimmed string must parse (JS Number is strict;
        // "12px" → NaN), unlike parseFloat. NumberStyles.Float allows a leading sign + exponent
        // but no thousands separators; AllowLeadingSign covers "+1"/"-1".
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        if (double.TryParse(t, style, CultureInfo.InvariantCulture, out double d))
            return d;

        return double.NaN;
    }

    /// <summary>
    /// JS <c>parseFloat</c>: leading-number parse, NaN on no leading number. Used by app.js
    /// <c>num()</c> / <c>parseFloat</c> call sites in the Web layer.
    /// </summary>
    public static double ParseFloat(string? s)
    {
        if (s is null) return double.NaN;
        string t = s.TrimStart();
        if (t.Length == 0) return double.NaN;

        if (t.StartsWith("Infinity", StringComparison.Ordinal)) return double.PositiveInfinity;
        if (t.StartsWith("+Infinity", StringComparison.Ordinal)) return double.PositiveInfinity;
        if (t.StartsWith("-Infinity", StringComparison.Ordinal)) return double.NegativeInfinity;

        // Find the longest leading prefix that is a valid JS float literal.
        int end = 0;
        int n = t.Length;
        int i = 0;
        if (i < n && (t[i] == '+' || t[i] == '-')) i++;
        int digitsBefore = 0, digitsAfter = 0;
        while (i < n && char.IsAsciiDigit(t[i])) { i++; digitsBefore++; }
        if (i < n && t[i] == '.') { i++; while (i < n && char.IsAsciiDigit(t[i])) { i++; digitsAfter++; } }
        if (digitsBefore == 0 && digitsAfter == 0) return double.NaN;
        end = i;
        // optional exponent — only kept if well-formed
        if (i < n && (t[i] == 'e' || t[i] == 'E'))
        {
            int j = i + 1;
            if (j < n && (t[j] == '+' || t[j] == '-')) j++;
            int expDigits = 0;
            while (j < n && char.IsAsciiDigit(t[j])) { j++; expDigits++; }
            if (expDigits > 0) end = j;
        }

        string token = t[..end];
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        return double.TryParse(token, style, CultureInfo.InvariantCulture, out double d) ? d : double.NaN;
    }

    /// <summary>JS <c>isFinite(Number(v))</c> — <c>typeof number &amp;&amp; isFinite</c> in the engine.</summary>
    public static bool IsFiniteNumber(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
}
