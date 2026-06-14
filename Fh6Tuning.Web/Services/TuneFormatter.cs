using System.Globalization;
using Fh6Tuning.Core;

namespace Fh6Tuning.Web.Services;

/// <summary>One key/value display row in an output card (legacy <c>{ k, v, sub }</c>).</summary>
public readonly record struct TuneRow(string Key, string Value, bool Sub = false);

/// <summary>
/// A single-goal output card definition (legacy <c>CARDS[i]</c>): the icon + title shown in the
/// header, a function that produces its display rows from a tune, and the section's why selector.
/// </summary>
public sealed record CardDef(
    string Id,
    string Title,
    string Icon,
    Func<Tune, IReadOnlyList<TuneRow>> Rows,
    Func<Tune, Why?> Why);

/// <summary>
/// A compare-table row definition (legacy <c>compareRowDefs[i]</c>). A group header
/// (<see cref="Group"/> non-null) spans every column; otherwise <see cref="Label"/> + <see cref="Get"/>
/// produce the per-goal cells.
/// </summary>
public sealed record CompareRow(string? Group, string? Label, Func<Tune, string>? Get)
{
    public static CompareRow GroupHeader(string group) => new(group, null, null);
    public static CompareRow Cell(string label, Func<Tune, string> get) => new(null, label, get);
}

/// <summary>A plain-language "what the sliders changed" effect (legacy <c>effectPhrase</c> result).</summary>
public readonly record struct ChangeEffect(string Text, string From, string To, bool Up)
{
    public string Dir => Up ? "up" : "down";
}

/// <summary>
/// Port of the display/formatting layer of <c>legacy/app.js</c> (lines 155-217 CARDS + gearRows/
/// diffRows, 246-310 changes panel, 312-372 compareRowDefs, 387-412 tuneToText). Produces the exact
/// row labels, unit suffixes and number formatting the legacy UI showed. Stateless apart from the
/// injected <see cref="UnitService"/>; the active <see cref="UnitSystem"/> is passed per call so the
/// metric toggle re-renders correctly.
/// </summary>
public sealed class TuneFormatter
{
    private readonly UnitService _u;

    public TuneFormatter(UnitService units) => _u = units;

    // shorthand wrappers binding the active unit system (mirror the legacy free functions)
    private string Nf(double? v, int dp = 1) => _u.Nf(v, dp);
    private string SpringDisp(double vImp, UnitSystem un) => _u.SpringDisp(vImp, un);
    private string RideDisp(double vImp, UnitSystem un) => _u.RideDisp(vImp, un);
    private string AeroDisp(double vImp, UnitSystem un) => _u.AeroDisp(vImp, un);
    private string SpeedDisp(double mphImp, UnitSystem un) => _u.SpeedDisp(mphImp, un);

    /// <summary>JS <c>String(n)</c> for a whole-number-ish output used bare (ARB/diff/braking %),
    /// shortest round-trip, invariant.</summary>
    private static string S(double n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// legacy <c>aeroVal(pct, lbfImp, absent)</c> (app.js:55): lbf (with % in parens) if a range was
    /// given, else "% of range", else the absent label. <paramref name="pct"/> null → absent.
    /// </summary>
    public string AeroVal(double? pct, double? lbfImp, string absent, UnitSystem un)
    {
        if (pct is null) return absent;
        // legacy interpolates pct raw (${pct}%), not via nf — the engine already emits an integer.
        return lbfImp is not null
            ? $"{AeroDisp(lbfImp.Value, un)} ({S(pct.Value)}%)"
            : $"{S(pct.Value)}% of range";
    }

    // ============================================================================================
    //  Single-goal card rows (legacy gearRows / diffRows / CARDS, app.js:156-217)
    // ============================================================================================

    /// <summary>legacy <c>gearRows(t)</c> (app.js:156-177).</summary>
    public IReadOnlyList<TuneRow> GearRows(Tune t, UnitSystem un)
    {
        var g = t.Gearing;
        var rows = new List<TuneRow> { new("Final drive", Nf(g.Final, 2)) };
        if (g.SingleSpeed)
        {
            // EVs are single-speed but the lone gear IS an adjustable in-game slider ("1st").
            rows.Add(new TuneRow(
                "1st (only gear)",
                Nf(g.Ratios[0], 2) + (g.Speeds is not null ? $"  ·  {SpeedDisp(g.Speeds[0], un)}" : ""),
                Sub: true));
        }
        else
        {
            for (int idx = 0; idx < g.Ratios.Count; idx++)
            {
                rows.Add(new TuneRow(
                    $"Gear {idx + 1}",
                    Nf(g.Ratios[idx], 2) + (g.Speeds is not null ? $"  ·  {SpeedDisp(g.Speeds[idx], un)}" : ""),
                    Sub: true));
            }
        }
        if (g.TopSpeed is not null) rows.Add(new TuneRow("Top speed @ redline", SpeedDisp(g.TopSpeed.Value, un)));
        return rows;
    }

    /// <summary>legacy <c>diffRows(t)</c> (app.js:178-188).</summary>
    public IReadOnlyList<TuneRow> DiffRows(Tune t)
    {
        var dd = t.Differential;
        if (dd.Driveline == Drivetrain.AWD)
        {
            return new[]
            {
                new TuneRow("Front accel", S(dd.FrontAccel ?? 0) + "%"),
                new TuneRow("Front decel", S(dd.FrontDecel ?? 0) + "%"),
                new TuneRow("Rear accel", S(dd.Accel) + "%"),
                new TuneRow("Rear decel", S(dd.Decel) + "%"),
                new TuneRow("Center (→rear)", S(dd.CenterRear ?? 0) + "%"),
            };
        }
        return new[]
        {
            new TuneRow(dd.Driveline + " accel", S(dd.Accel) + "%"),
            new TuneRow(dd.Driveline + " decel", S(dd.Decel) + "%"),
        };
    }

    /// <summary>
    /// The 9 single-goal cards in legacy order (legacy <c>CARDS</c>, app.js:190-217). Row functions
    /// bind the active unit system so spring/ride/aero/speed display re-renders on the metric toggle.
    /// </summary>
    public IReadOnlyList<CardDef> Cards(UnitSystem un) => new CardDef[]
    {
        new("tires", "Tires", "🛞",
            t => new[]
            {
                new TuneRow("Front pressure", Nf(t.Tires.Front, 1) + " psi"),
                new TuneRow("Rear pressure", Nf(t.Tires.Rear, 1) + " psi"),
            },
            t => t.Tires.Why),
        new("gearing", "Gearing", "⚙️", t => GearRows(t, un), t => t.Gearing.Why),
        new("alignment", "Alignment", "📐",
            t => new[]
            {
                new TuneRow("Camber front", Nf(t.Alignment.CamberF, 1) + "°"),
                new TuneRow("Camber rear", Nf(t.Alignment.CamberR, 1) + "°"),
                new TuneRow("Toe front", Nf(t.Alignment.ToeF, 1) + "°"),
                new TuneRow("Toe rear", Nf(t.Alignment.ToeR, 1) + "°"),
                new TuneRow("Caster", Nf(t.Alignment.Caster, 1) + "°"),
            },
            t => t.Alignment.Why),
        new("arb", "Anti-Roll Bars", "🌀",
            t => new[]
            {
                new TuneRow("Front", Nf(t.Arb.Front, 0)),
                new TuneRow("Rear", Nf(t.Arb.Rear, 0)),
            },
            t => t.Arb.Why),
        new("springs", "Springs & Ride Height", "🔧",
            t => new[]
            {
                new TuneRow("Front rate", SpringDisp(t.Springs.Front, un) + " " + (un == UnitSystem.Metric ? "kgf/mm" : "lb/in")),
                new TuneRow("Rear rate", SpringDisp(t.Springs.Rear, un) + " " + (un == UnitSystem.Metric ? "kgf/mm" : "lb/in")),
                new TuneRow("Ride height F", RideDisp(t.Springs.RideF, un) + (un == UnitSystem.Metric ? " cm" : " in")),
                new TuneRow("Ride height R", RideDisp(t.Springs.RideR, un) + (un == UnitSystem.Metric ? " cm" : " in")),
            },
            t => t.Springs.Why),
        new("damping", "Damping", "〽️",
            t => new[]
            {
                new TuneRow("Rebound front", Nf(t.Damping.ReboundF, 1)),
                new TuneRow("Rebound rear", Nf(t.Damping.ReboundR, 1)),
                new TuneRow("Bump front", Nf(t.Damping.BumpF, 1)),
                new TuneRow("Bump rear", Nf(t.Damping.BumpR, 1)),
            },
            t => t.Damping.Why),
        new("aero", "Aero", "🪽",
            t => !t.Aero.Applicable
                ? new[] { new TuneRow("Aero", "Not installed") }
                : new[]
                {
                    new TuneRow("Front downforce", AeroVal(t.Aero.Front, t.Aero.FrontLbf, "— (no splitter)", un)),
                    new TuneRow("Rear downforce", AeroVal(t.Aero.Rear, t.Aero.RearLbf, "— (no wing)", un)),
                },
            t => t.Aero.Why),
        new("braking", "Braking", "🛑",
            t => new[]
            {
                new TuneRow("Balance (→front)", S(t.Braking.Balance) + "%"),
                new TuneRow("Pressure", S(t.Braking.Pressure) + "%"),
            },
            t => t.Braking.Why),
        new("differential", "Differential", "🔩", t => DiffRows(t), t => t.Differential.Why),
    };

    // ============================================================================================
    //  "What the sliders changed" diff (legacy endOf / effectPhrase, app.js:248-281)
    // ============================================================================================

    /// <summary>legacy <c>endOf(key)</c> (app.js:248-252): which end of the car a row label refers to.</summary>
    private static string EndOf(string key)
    {
        // /front/i || /\bF$/  → "front" ; /rear/i || /\bR$/ → "rear" ; else ""
        if (key.Contains("front", StringComparison.OrdinalIgnoreCase) || EndsWithWordChar(key, 'F'))
            return "front";
        if (key.Contains("rear", StringComparison.OrdinalIgnoreCase) || EndsWithWordChar(key, 'R'))
            return "rear";
        return "";
    }

    // JS /\bF$/ — the key ends in F (or R) on a word boundary. All legacy keys ending in F/R have a
    // non-word char (space) before the letter, so a trailing standalone "F"/"R" qualifies.
    private static bool EndsWithWordChar(string key, char c) =>
        key.Length > 0 && key[^1] == c && (key.Length == 1 || !char.IsLetterOrDigit(key[^2]) && key[^2] != '_');

    /// <summary>legacy <c>effectPhrase(cardId, key, fromStr, toStr)</c> (app.js:254-281).</summary>
    public ChangeEffect EffectPhrase(string cardId, string key, string fromStr, string toStr)
    {
        double f = JsNumber.ParseFloat(fromStr);
        double tv = JsNumber.ParseFloat(toStr);
        // up = !(isFinite(f) && isFinite(tv)) || tv >= f
        bool up = !(JsNumber.IsFiniteNumber(f) && JsNumber.IsFiniteNumber(tv)) || tv >= f;
        string end = EndOf(key);
        string text;
        switch (cardId)
        {
            case "arb":
                text = $"{(up ? "stiffer" : "softer")} {end} anti-roll bar";
                break;
            case "springs":
                text = key.Contains("rate", StringComparison.OrdinalIgnoreCase)
                    ? $"{(up ? "stiffer" : "softer")} {end} springs"
                    : $"{(up ? "higher" : "lower")} {end} ride height";
                break;
            case "damping":
                text = $"{(up ? "firmer" : "softer")} {end} {(key.Contains("bump", StringComparison.OrdinalIgnoreCase) ? "bump" : "rebound")}";
                break;
            case "braking":
                text = up ? "more front brake bias" : "more rear brake bias";
                break;
            case "differential":
                if (key.Contains("center", StringComparison.OrdinalIgnoreCase))
                    text = up ? "more torque to the rear" : "more torque to the front";
                else
                    text = $"{(up ? "more" : "less")} {(end.Length > 0 ? end + " " : "")}{(key.Contains("accel", StringComparison.OrdinalIgnoreCase) ? "accel" : "decel")} lock";
                break;
            case "aero":
                text = $"{(up ? "more" : "less")} {end} downforce";
                break;
            default:
                text = $"{key} {(up ? "increased" : "decreased")}";
                break;
        }
        return new ChangeEffect(text, fromStr, toStr, up);
    }

    /// <summary>
    /// legacy <c>renderChangesPanel</c> diff loop (app.js:283-293): every card row whose live value
    /// differs from the centered-baseline value, in card order, as a plain-language effect.
    /// </summary>
    public IReadOnlyList<ChangeEffect> CollectChanges(Tune live, Tune baseline, UnitSystem un)
    {
        var items = new List<ChangeEffect>();
        foreach (var c in Cards(un))
        {
            var liveRows = c.Rows(live);
            var baseRows = c.Rows(baseline);
            for (int i = 0; i < liveRows.Count; i++)
            {
                if (i >= baseRows.Count || baseRows[i].Value == liveRows[i].Value) continue;
                items.Add(EffectPhrase(c.Id, liveRows[i].Key, baseRows[i].Value, liveRows[i].Value));
            }
        }
        return items;
    }

    // ============================================================================================
    //  Compare table (legacy compareRowDefs, app.js:312-372)
    // ============================================================================================

    /// <summary>
    /// legacy <c>compareRowDefs(input)</c> (app.js:313-372). Branches on the input's powertrain
    /// (EV ⇒ no per-gear rows), redline+tireDiameter (⇒ top-speed row), aero-range presence (⇒ lbf vs
    /// % header) and drivetrain (AWD ⇒ 5 diff rows, else 2).
    /// </summary>
    public IReadOnlyList<CompareRow> CompareRowDefs(TuneInput input, UnitSystem un)
    {
        // legacy: const n = input.gears (the intField'd gear count); drives the per-gear row loop.
        int n = (int)input.Gears;
        bool ev = input.Powertrain == Powertrain.EV;
        bool aeroFrontLbf = input.HasFrontAero && input.AeroFront.Min is not null;
        bool aeroRearLbf = input.HasRearAero && input.AeroRear.Min is not null;

        var defs = new List<CompareRow>
        {
            CompareRow.GroupHeader("Tires"),
            CompareRow.Cell("Front psi", t => Nf(t.Tires.Front, 1)),
            CompareRow.Cell("Rear psi", t => Nf(t.Tires.Rear, 1)),
            CompareRow.GroupHeader("Gearing"),
            CompareRow.Cell("Final drive", t => Nf(t.Gearing.Final, 2)),
        };

        if (!ev)
        {
            for (int g = 0; g < n; g++)
            {
                int gi = g; // capture
                defs.Add(CompareRow.Cell($"Gear {gi + 1}",
                    t => gi < t.Gearing.Ratios.Count ? Nf(t.Gearing.Ratios[gi], 2) : "—"));
            }
        }
        if (input.RedlineRpm is > 0 && input.TireDiameter is > 0)
        {
            defs.Add(CompareRow.Cell($"Top speed ({(un == UnitSystem.Metric ? "km/h" : "mph")})",
                t => t.Gearing.TopSpeed is not null ? Nf(_u.FromImp(UnitService.Dim.Speed, t.Gearing.TopSpeed.Value, un), 0) : "—"));
        }

        defs.AddRange(new[]
        {
            CompareRow.GroupHeader("Alignment"),
            CompareRow.Cell("Camber F", t => Nf(t.Alignment.CamberF, 1) + "°"),
            CompareRow.Cell("Camber R", t => Nf(t.Alignment.CamberR, 1) + "°"),
            CompareRow.Cell("Toe F", t => Nf(t.Alignment.ToeF, 1) + "°"),
            CompareRow.Cell("Toe R", t => Nf(t.Alignment.ToeR, 1) + "°"),
            CompareRow.Cell("Caster", t => Nf(t.Alignment.Caster, 1) + "°"),
            CompareRow.GroupHeader("Anti-Roll Bars"),
            CompareRow.Cell("ARB Front", t => Nf(t.Arb.Front, 0)),
            CompareRow.Cell("ARB Rear", t => Nf(t.Arb.Rear, 0)),
            CompareRow.GroupHeader($"Springs ({(un == UnitSystem.Metric ? "kgf/mm" : "lb/in")})"),
            CompareRow.Cell("Front rate", t => SpringDisp(t.Springs.Front, un)),
            CompareRow.Cell("Rear rate", t => SpringDisp(t.Springs.Rear, un)),
            CompareRow.Cell($"Ride F ({(un == UnitSystem.Metric ? "cm" : "in")})", t => RideDisp(t.Springs.RideF, un)),
            CompareRow.Cell($"Ride R ({(un == UnitSystem.Metric ? "cm" : "in")})", t => RideDisp(t.Springs.RideR, un)),
            CompareRow.GroupHeader("Damping"),
            CompareRow.Cell("Rebound F", t => Nf(t.Damping.ReboundF, 1)),
            CompareRow.Cell("Rebound R", t => Nf(t.Damping.ReboundR, 1)),
            CompareRow.Cell("Bump F", t => Nf(t.Damping.BumpF, 1)),
            CompareRow.Cell("Bump R", t => Nf(t.Damping.BumpR, 1)),
            CompareRow.GroupHeader("Aero"),
            CompareRow.Cell(aeroFrontLbf ? $"Front DF ({(un == UnitSystem.Metric ? "kgf" : "lbf")})" : "Front DF",
                t => (!t.Aero.Applicable || t.Aero.Front is null) ? "—"
                    : (aeroFrontLbf && t.Aero.FrontLbf is not null
                        ? Nf(_u.FromImp(UnitService.Dim.Aero, t.Aero.FrontLbf.Value, un), 0)
                        : S(t.Aero.Front.Value) + "%")),
            CompareRow.Cell(aeroRearLbf ? $"Rear DF ({(un == UnitSystem.Metric ? "kgf" : "lbf")})" : "Rear DF",
                t => (!t.Aero.Applicable || t.Aero.Rear is null) ? "—"
                    : (aeroRearLbf && t.Aero.RearLbf is not null
                        ? Nf(_u.FromImp(UnitService.Dim.Aero, t.Aero.RearLbf.Value, un), 0)
                        : S(t.Aero.Rear.Value) + "%")),
            CompareRow.GroupHeader("Braking"),
            CompareRow.Cell("Balance", t => S(t.Braking.Balance) + "%"),
            CompareRow.Cell("Pressure", t => S(t.Braking.Pressure) + "%"),
            CompareRow.GroupHeader("Differential"),
        });

        if (input.Drivetrain == Drivetrain.AWD)
        {
            defs.AddRange(new[]
            {
                CompareRow.Cell("Front accel", t => (t.Differential.FrontAccel is null ? "—" : S(t.Differential.FrontAccel.Value)) + "%"),
                CompareRow.Cell("Front decel", t => (t.Differential.FrontDecel is null ? "—" : S(t.Differential.FrontDecel.Value)) + "%"),
                CompareRow.Cell("Rear accel", t => S(t.Differential.Accel) + "%"),
                CompareRow.Cell("Rear decel", t => S(t.Differential.Decel) + "%"),
                CompareRow.Cell("Center→rear", t => (t.Differential.CenterRear is null ? "—" : S(t.Differential.CenterRear.Value)) + "%"),
            });
        }
        else
        {
            defs.AddRange(new[]
            {
                CompareRow.Cell("Accel lock", t => S(t.Differential.Accel) + "%"),
                CompareRow.Cell("Decel lock", t => S(t.Differential.Decel) + "%"),
            });
        }

        return defs;
    }

    // ============================================================================================
    //  Copy-to-text (legacy tuneToText, app.js:387-412)
    // ============================================================================================

    /// <summary>legacy <c>tuneToText(t, input)</c> (app.js:387-412) — the copy-tune plaintext block.</summary>
    public string TuneToText(Tune t, TuneInput input, GoalMeta goalMeta, UnitSystem un)
    {
        var L = new List<string>();
        L.Add($"FH6 TUNE — {goalMeta.Label}");
        L.Add($"Car: {input.Drivetrain} {input.EngineLocation}-engine {input.Powertrain}, {Nf(t.Derived.Frac * 100, 0)}% front, P/W {Nf(t.Derived.Pw, 2)} hp/lb");

        if (input.HandlingBias != 0 || input.OverallStiffness != 0)
        {
            var parts = new List<string>();
            if (input.HandlingBias != 0)
                parts.Add($"bias {(input.HandlingBias > 0 ? "+" : "")}{Nf(input.HandlingBias, 1)} ({(input.HandlingBias > 0 ? "oversteer" : "understeer")})");
            if (input.OverallStiffness != 0)
                parts.Add($"stiffness {(input.OverallStiffness > 0 ? "+" : "")}{Nf(input.OverallStiffness, 1)} ({(input.OverallStiffness > 0 ? "hard" : "soft")})");
            L.Add($"Dials: {string.Join(", ", parts)}");
        }

        L.Add($"— Tires: F {Nf(t.Tires.Front, 1)} / R {Nf(t.Tires.Rear, 1)} psi");

        string gearStr = t.Gearing.SingleSpeed
            ? $"  1st (only gear): {Nf(t.Gearing.Ratios[0], 2)}"
            : "  Gears: " + string.Join(", ", t.Gearing.Ratios.Select(r => Nf(r, 2)));
        string topStr = t.Gearing.TopSpeed is not null ? $"  | Top speed ~{SpeedDisp(t.Gearing.TopSpeed.Value, un)}" : "";
        L.Add($"— Final drive: {Nf(t.Gearing.Final, 2)}{gearStr}{topStr}");

        L.Add($"— Camber: F {Nf(t.Alignment.CamberF, 1)} / R {Nf(t.Alignment.CamberR, 1)}  Toe: F {Nf(t.Alignment.ToeF, 1)} / R {Nf(t.Alignment.ToeR, 1)}  Caster: {Nf(t.Alignment.Caster, 1)}");
        L.Add($"— ARB: F {S(t.Arb.Front)} / R {S(t.Arb.Rear)}");
        L.Add($"— Springs: F {SpringDisp(t.Springs.Front, un)} / R {SpringDisp(t.Springs.Rear, un)} {(un == UnitSystem.Metric ? "kgf/mm" : "lb/in")}  Ride: F {RideDisp(t.Springs.RideF, un)} / R {RideDisp(t.Springs.RideR, un)} {(un == UnitSystem.Metric ? "cm" : "in")}");
        L.Add($"— Damping: Reb F {Nf(t.Damping.ReboundF, 1)} / R {Nf(t.Damping.ReboundR, 1)}  Bump F {Nf(t.Damping.BumpF, 1)} / R {Nf(t.Damping.BumpR, 1)}");

        string af = t.Aero.Front is null ? "n/a" : (t.Aero.FrontLbf is not null ? AeroDisp(t.Aero.FrontLbf.Value, un) : S(t.Aero.Front.Value) + "%");
        string ar = t.Aero.Rear is null ? "n/a" : (t.Aero.RearLbf is not null ? AeroDisp(t.Aero.RearLbf.Value, un) : S(t.Aero.Rear.Value) + "%");
        L.Add($"— Aero: {(!t.Aero.Applicable ? "none installed" : $"F {af} / R {ar}")}");

        L.Add($"— Brakes: balance {S(t.Braking.Balance)}% front, pressure {S(t.Braking.Pressure)}%");

        if (t.Differential.Driveline == Drivetrain.AWD)
            L.Add($"— Diff: front {S(t.Differential.FrontAccel ?? 0)}/{S(t.Differential.FrontDecel ?? 0)}%, rear {S(t.Differential.Accel)}/{S(t.Differential.Decel)}%, center {S(t.Differential.CenterRear ?? 0)}% rear");
        else
            L.Add($"— Diff: {t.Differential.Driveline} accel {S(t.Differential.Accel)}% / decel {S(t.Differential.Decel)}%");

        return string.Join("\n", L);
    }
}
