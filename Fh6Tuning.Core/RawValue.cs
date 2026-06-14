namespace Fh6Tuning.Core;

/// <summary>Discriminator for <see cref="RawValue"/>.</summary>
public enum RawKind { Absent, Str, Num, Bool }

/// <summary>
/// A single raw input value as <c>validate()</c> sees it: absent (undefined/null/missing key),
/// a string (a DOM <c>.value</c>, may be <c>""</c>), a <see cref="double"/> (incl. NaN/±Infinity),
/// or a bool. <c>JsNumber.Coerce(this)</c> == JS <c>Number(v)</c>.
///
/// The Web layer fills these from the form (always strings); the parity/failure tests fill them
/// directly (numbers/NaN/Infinity/bools).
/// </summary>
public readonly record struct RawValue
{
    public RawKind Kind { get; }
    private readonly string? _str;
    private readonly double _num;
    private readonly bool _bool;

    private RawValue(RawKind kind, string? str, double num, bool b)
    {
        Kind = kind;
        _str = str;
        _num = num;
        _bool = b;
    }

    /// <summary>undefined / null / missing key.</summary>
    public static readonly RawValue Absent = new(RawKind.Absent, null, 0, false);

    /// <summary>A DOM <c>.value</c> (may be <c>""</c>). A null string is treated as Absent.</summary>
    public static RawValue Str(string? s) =>
        s is null ? Absent : new RawValue(RawKind.Str, s, 0, false);

    /// <summary>A real number (NaN / ±Infinity pass through unchanged).</summary>
    public static RawValue Num(double d) => new(RawKind.Num, null, d, false);

    /// <summary>A boolean.</summary>
    public static RawValue Bool(bool b) => new(RawKind.Bool, null, 0, b);

    public bool IsAbsent => Kind == RawKind.Absent;

    public string? AsString => _str;
    public double AsNumber => _num;
    public bool AsBool => _bool;
}
