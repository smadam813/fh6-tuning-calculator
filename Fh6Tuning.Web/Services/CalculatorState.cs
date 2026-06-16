using System.Globalization;
using System.Text.Json.Nodes;
using Fh6Tuning.Core;

namespace Fh6Tuning.Web.Services;

/// <summary>
/// The live UI state container — replaces the implicit DOM state of legacy/app.js. A plain
/// observable (<see cref="Changed"/>); every <c>Set*</c> raises it once so components can
/// <c>StateHasChanged()</c>, reproducing the legacy "live updates on every input".
///
/// Holds:
///  • <see cref="Units"/>, <see cref="CurrentGoal"/>, <see cref="CompareMode"/>,
///    <see cref="HandlingBias"/>, <see cref="OverallStiffness"/>;
///  • the categorical selects (drivetrain / engine location / powertrain / PI class /
///    tire compound / suspension / aero kit);
///  • a string-backed <see cref="RawForm"/> — every numeric form field as a string keyed by the
///    legacy element id (blank = empty). The parse/convert layers (later UI tasks) read these.
///
/// Initial values mirror the legacy index.html defaults: imperial units, RWD / Front / ICE / A /
/// Sport / Race / Full aero kit, both dials neutral (0), compare off, Circuit goal, all numeric
/// fields blank.
/// </summary>
public sealed class CalculatorState
{
    private readonly UnitService _units;

    public CalculatorState(UnitService units) => _units = units;

    /// <summary>Raised once per state mutation. Components subscribe and re-render.</summary>
    public event Action? Changed;

    private void Notify() => Changed?.Invoke();

    // ---------- units ----------
    private UnitSystem _unitSystem = UnitSystem.Imperial;
    public UnitSystem Units => _unitSystem;

    // ---------- output controls ----------
    public Goal CurrentGoal { get; private set; } = Goal.Circuit;
    public bool CompareMode { get; private set; }

    // ---------- the two dials (0 = pure per-goal baseline) ----------
    public double HandlingBias { get; private set; }
    public double OverallStiffness { get; private set; }

    // ---------- categorical selects (legacy index.html defaults) ----------
    public Drivetrain Drivetrain { get; private set; } = Drivetrain.RWD;
    public EngineLocation EngineLocation { get; private set; } = EngineLocation.Front;
    public Powertrain Powertrain { get; private set; } = Powertrain.ICE;
    public PiClass PiClass { get; private set; } = PiClass.A;
    public TireCompound TireCompound { get; private set; } = TireCompound.Sport;
    public SuspensionType SuspensionType { get; private set; } = SuspensionType.Race;
    public AeroKit AeroKit { get; private set; } = AeroKit.Full;

    // ---------- string-backed numeric form fields (keyed by legacy element id) ----------
    // Every value is the raw <input> string ("" = blank). Mirrors what a DOM .value carries.
    private readonly Dictionary<string, string> _rawForm = NewBlankForm();

    /// <summary>Read-only view of the raw string form, keyed by legacy element id.</summary>
    public IReadOnlyDictionary<string, string> RawForm => _rawForm;

    /// <summary>The complete set of numeric form-field ids (the keys of <see cref="RawForm"/>).</summary>
    public static IReadOnlyList<string> FieldIds { get; } = new[]
    {
        // Performance
        "power", "torque", "weight", "frontWeight", "gears",
        // Installed parts — spring rate / ride height ranges (per axle)
        "springRateMinF", "springRateMaxF", "springRateMinR", "springRateMaxR",
        "rideHeightMinF", "rideHeightMaxF", "rideHeightMinR", "rideHeightMaxR",
        // Aero downforce ranges
        "aeroFrontMin", "aeroFrontMax", "aeroRearMin", "aeroRearMax",
        // Gearing refinement
        "redlineRpm", "peakPowerRpm", "maxTorqueRpm", "targetTopSpeed", "tireWidth", "tireAspect", "tireRim",
    };

    private static Dictionary<string, string> NewBlankForm()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in FieldIds) d[id] = "";
        return d;
    }

    /// <summary>Raw string for a numeric field id ("" if blank/unknown).</summary>
    public string GetField(string id) => _rawForm.TryGetValue(id, out var v) ? v : "";

    /// <summary>Sets a numeric field's raw string and notifies (no-op if unchanged). Ignores ids that
    /// aren't <see cref="FieldIds"/> keys: <c>_rawForm</c> must stay exactly the numeric-field set so
    /// <c>ApplySetup</c>'s <c>_rawForm.ContainsKey</c> routing never mistakes a categorical select id
    /// (e.g. "aeroKit") for a numeric field.</summary>
    public void SetField(string id, string? value)
    {
        if (!_rawForm.TryGetValue(id, out var cur)) return;
        var next = value ?? "";
        if (cur == next) return;
        _rawForm[id] = next;
        Notify();
    }

    // ---------- validation / welcome gate (legacy app.js:447-452, 84-93) ----------

    /// <summary>
    /// The three stats a tune can't be derived without (legacy <c>REQUIRED_FIELDS</c>). True until
    /// every one is filled — a first-time visitor sees a friendly welcome rather than red errors.
    /// A raw-string emptiness check, run BEFORE any parse ("incomplete ≠ invalid").
    /// </summary>
    public bool IsIncomplete() =>
        GetField("power").Trim().Length == 0
        || GetField("weight").Trim().Length == 0
        || GetField("frontWeight").Trim().Length == 0;

    /// <summary>
    /// Builds the raw, loosely-typed <see cref="RawInput"/> the engine's <c>validate()</c> reads —
    /// every form field as the raw <c>.value</c> string (blank → <c>RawValue.Str("")</c>). Mirrors
    /// what legacy validate() saw off the DOM (legacy/app.js:84-93). Validation is unit-agnostic
    /// (it only checks finiteness / sign / ordering), so the strings flow through verbatim.
    /// </summary>
    public RawInput BuildRawInput() => new()
    {
        Power = RawValue.Str(GetField("power")),
        Torque = RawValue.Str(GetField("torque")),
        Weight = RawValue.Str(GetField("weight")),
        FrontWeightPct = RawValue.Str(GetField("frontWeight")),
        Gears = RawValue.Str(GetField("gears")),
        SpringRateMinF = RawValue.Str(GetField("springRateMinF")),
        SpringRateMaxF = RawValue.Str(GetField("springRateMaxF")),
        SpringRateMinR = RawValue.Str(GetField("springRateMinR")),
        SpringRateMaxR = RawValue.Str(GetField("springRateMaxR")),
        RideHeightMinF = RawValue.Str(GetField("rideHeightMinF")),
        RideHeightMaxF = RawValue.Str(GetField("rideHeightMaxF")),
        RideHeightMinR = RawValue.Str(GetField("rideHeightMinR")),
        RideHeightMaxR = RawValue.Str(GetField("rideHeightMaxR")),
    };

    // ---------- engine-ready (imperial) input (legacy readInputs, app.js:84-123) ----------

    /// <summary>
    /// Builds the imperial <see cref="TuneInput"/> the engine's <c>compute</c> reads — a faithful port
    /// of legacy <c>readInputs()</c> (app.js:84-123). Metric form values are converted to imperial via
    /// <see cref="UnitService.ToImp"/>; the parse helpers reproduce <c>num</c> / <c>rangeField</c> /
    /// <c>optField</c> / <c>optAeroRange</c> / <c>intField</c> including their blank-field defaults; the
    /// aero kit explodes into the three engine booleans; tire diameter comes from the FH6 width/aspect/
    /// rim spec. The two dials come from <see cref="HandlingBias"/>/<see cref="OverallStiffness"/>.
    ///
    /// <para>Call only when inputs are complete and valid (the welcome/validate gate runs first), just
    /// like legacy <c>refresh()</c>.</para>
    /// </summary>
    public TuneInput BuildTuneInput() => new()
    {
        Drivetrain = Drivetrain,
        EngineLocation = EngineLocation,
        Powertrain = Powertrain,
        PiClass = PiClass,
        TireCompound = TireCompound,
        SuspensionType = SuspensionType,

        Power = Num("power"),
        Torque = ToImp(UnitService.Dim.Torque, Num("torque")),
        Weight = ToImp(UnitService.Dim.Weight, Num("weight")),
        FrontWeightPct = Num("frontWeight"),
        Gears = IntField("gears", 6),

        // aero kit: None | Front (splitter only) | Rear (wing only) | Full
        HasFrontAero = AeroKit is AeroKit.Front or AeroKit.Full,
        HasRearAero = AeroKit is AeroKit.Rear or AeroKit.Full,
        AeroInstalled = AeroKit != AeroKit.None, // any wing -> secondary downforce effects apply

        RideHeightMinF = RangeField("rideHeightMinF", UnitService.Dim.Ride, 4.5),
        RideHeightMaxF = RangeField("rideHeightMaxF", UnitService.Dim.Ride, 7.0),
        RideHeightMinR = RangeField("rideHeightMinR", UnitService.Dim.Ride, 4.5),
        RideHeightMaxR = RangeField("rideHeightMaxR", UnitService.Dim.Ride, 7.0),
        SpringRateMinF = RangeField("springRateMinF", UnitService.Dim.Spring, 150),
        SpringRateMaxF = RangeField("springRateMaxF", UnitService.Dim.Spring, 900),
        SpringRateMinR = RangeField("springRateMinR", UnitService.Dim.Spring, 150),
        SpringRateMaxR = RangeField("springRateMaxR", UnitService.Dim.Spring, 900),

        // optional downforce ranges (imperial lbf, or AeroRange.None = show % of slider)
        AeroFront = OptAeroRange("aeroFrontMin", "aeroFrontMax"),
        AeroRear = OptAeroRange("aeroRearMin", "aeroRearMax"),

        // optional gearing physics (null = HP heuristic). target speed in mph; tire Ø in inches.
        // rpm fields are unit-independent (no dim) — exactly like redlineRpm.
        RedlineRpm = OptField("redlineRpm", null),
        PeakPowerRpm = OptField("peakPowerRpm", null),
        MaxTorqueRpm = OptField("maxTorqueRpm", null),
        TireDiameter = TuneInput.OverallTireDiameter(
            OptField("tireWidth", null), OptField("tireAspect", null), OptField("tireRim", null)),
        TargetTopSpeed = OptField("targetTopSpeed", UnitService.Dim.Speed),

        HandlingBias = HandlingBias,
        OverallStiffness = OverallStiffness,
    };

    private double ToImp(UnitService.Dim dim, double v) => _units.ToImp(dim, v, _unitSystem);

    /// <summary>legacy <c>num(id)</c> = <c>parseFloat(value) || 0</c>: blank / NaN / leading-non-number → 0.</summary>
    private double Num(string id)
    {
        double v = JsNumber.ParseFloat(GetField(id));
        // JS `|| 0`: 0, NaN (and -0) are falsy → 0; any other finite/Infinity passes through.
        return (double.IsNaN(v) || v == 0.0) ? 0.0 : v;
    }

    /// <summary>legacy <c>optField(id, dim)</c>: trimmed-blank or non-finite → null, else imperial value
    /// (via dim when supplied, raw otherwise).</summary>
    private double? OptField(string id, UnitService.Dim? dim)
    {
        string s = GetField(id).Trim();
        if (s.Length == 0) return null;
        double v = JsNumber.ParseFloat(s);
        if (!JsNumber.IsFiniteNumber(v)) return null;
        return dim is null ? v : ToImp(dim.Value, v);
    }

    /// <summary>legacy <c>rangeField(id, dim, defImp)</c>: blank → imperial default; finite → imperial
    /// value; non-finite → the imperial default.</summary>
    private double RangeField(string id, UnitService.Dim dim, double defImp)
    {
        string s = GetField(id).Trim();
        if (s.Length == 0) return defImp;
        double v = JsNumber.ParseFloat(s);
        return JsNumber.IsFiniteNumber(v) ? ToImp(dim, v) : defImp;
    }

    /// <summary>legacy <c>intField(id, def)</c>: blank → def; finite → Math.round; non-finite → def.</summary>
    private double IntField(string id, double def)
    {
        string s = GetField(id).Trim();
        if (s.Length == 0) return def;
        double v = JsNumber.ParseFloat(s);
        return JsNumber.IsFiniteNumber(v) ? JsMath.Round(v) : def;
    }

    /// <summary>legacy <c>optAeroRange(idMin, idMax)</c>: either end blank or non-finite →
    /// <see cref="AeroRange.None"/>, else [impMin, impMax].</summary>
    private AeroRange OptAeroRange(string idMin, string idMax)
    {
        string a = GetField(idMin).Trim(), b = GetField(idMax).Trim();
        if (a.Length == 0 || b.Length == 0) return AeroRange.None;
        double lo = JsNumber.ParseFloat(a), hi = JsNumber.ParseFloat(b);
        if (!JsNumber.IsFiniteNumber(lo) || !JsNumber.IsFiniteNumber(hi)) return AeroRange.None;
        return new AeroRange(ToImp(UnitService.Dim.Aero, lo), ToImp(UnitService.Dim.Aero, hi));
    }

    // ---------- setters (each raises Changed once) ----------
    public void SetCurrentGoal(Goal goal)
    {
        if (CurrentGoal == goal) return;
        CurrentGoal = goal;
        Notify();
    }

    public void SetCompareMode(bool on)
    {
        if (CompareMode == on) return;
        CompareMode = on;
        Notify();
    }

    public void SetHandlingBias(double v)
    {
        if (HandlingBias.Equals(v)) return;
        HandlingBias = v;
        Notify();
    }

    public void SetOverallStiffness(double v)
    {
        if (OverallStiffness.Equals(v)) return;
        OverallStiffness = v;
        Notify();
    }

    /// <summary>Reset the handling-bias dial to neutral (legacy <c>biasReset</c>).</summary>
    public void ResetHandlingBias() => SetHandlingBias(0);

    /// <summary>Reset the overall-stiffness dial to balanced (legacy <c>stiffReset</c>).</summary>
    public void ResetOverallStiffness() => SetOverallStiffness(0);

    public void SetDrivetrain(Drivetrain v) { if (Drivetrain != v) { Drivetrain = v; Notify(); } }
    public void SetEngineLocation(EngineLocation v) { if (EngineLocation != v) { EngineLocation = v; Notify(); } }
    public void SetPowertrain(Powertrain v) { if (Powertrain != v) { Powertrain = v; Notify(); } }
    public void SetPiClass(PiClass v) { if (PiClass != v) { PiClass = v; Notify(); } }
    public void SetTireCompound(TireCompound v) { if (TireCompound != v) { TireCompound = v; Notify(); } }
    public void SetSuspensionType(SuspensionType v) { if (SuspensionType != v) { SuspensionType = v; Notify(); } }
    public void SetAeroKit(AeroKit v) { if (AeroKit != v) { AeroKit = v; Notify(); } }

    /// <summary>
    /// Port of legacy <c>setUnits(next)</c> (app.js:504-516). Converts every unit-bound, non-blank
    /// field value so the same physical car is preserved, rounding per dimension so round-trips
    /// stay clean (ride → 1 dp; spring → 2 dp metric / 0 dp imperial; everything else → 0 dp),
    /// then flips the unit system. Labels are rebound by the components reading <see cref="Units"/>.
    /// Raises <see cref="Changed"/> once after the whole rewrite.
    /// </summary>
    public void SetUnits(UnitSystem next)
    {
        if (next == _unitSystem) return;

        foreach (var (id, dim) in UnitService.FieldDim)
        {
            if (!_rawForm.TryGetValue(id, out var raw)) continue;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue; // leave optional blanks blank

            // legacy: +el.value parsed leniently then ×/÷ factor.
            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                continue;

            double factor = _units.Factor(dim);
            double conv = next == UnitSystem.Metric ? v / factor : v * factor;

            int dp = dim switch
            {
                UnitService.Dim.Ride => 1,
                UnitService.Dim.Spring => next == UnitSystem.Metric ? 2 : 0,
                _ => 0,
            };

            // legacy: +conv.toFixed(dp) — round to dp then re-stringify with trailing zeros dropped.
            double rounded = double.Parse(
                conv.ToString("F" + dp.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);
            _rawForm[id] = FormatNumberLikeJs(rounded);
        }

        _unitSystem = next;
        Notify();
    }

    /// <summary>
    /// JS <c>String(+n)</c> for the converted field values: integral doubles render with no ".0"
    /// and no trailing zeros (e.g. 3300, 4.5, 16.07). Invariant-culture round-trip representation.
    /// </summary>
    private static string FormatNumberLikeJs(double n)
    {
        // "R" gives the shortest round-trippable form; matches JS Number→string for these magnitudes.
        return n.ToString("R", CultureInfo.InvariantCulture);
    }

    // =====================================================================================
    // Saved-setups snapshot / restore (legacy snapshotSetup / applySetup, app.js:584-615).
    //
    // A saved setup captures the FULL Car Setup panel: every categorical select + numeric field
    // (each by its legacy element id, value as a string), plus the active units, the current goal,
    // and both dial values. The categorical selects live alongside the numeric fields in the
    // legacy `fields` bag (they're `.inputs select` elements there), so we serialize them under the
    // same ids the legacy DOM used.
    // =====================================================================================

    /// <summary>The legacy element ids for the categorical selects, in DOM order — written into the
    /// snapshot's <c>fields</c> bag alongside the numeric fields, exactly as legacy did.</summary>
    private static readonly string[] SelectIds =
    {
        "drivetrain", "engineLocation", "powertrain", "piClass",
        "tireCompound", "suspensionType", "aeroKit",
    };

    /// <summary>The current value (legacy <c>.value</c> string) of a categorical select id — its
    /// enum member name, which equals the legacy <c>&lt;option value&gt;</c> token.</summary>
    private string GetSelect(string id) => id switch
    {
        "drivetrain" => Drivetrain.ToString(),
        "engineLocation" => EngineLocation.ToString(),
        "powertrain" => Powertrain.ToString(),
        "piClass" => PiClass.ToString(),
        "tireCompound" => TireCompound.ToString(),
        "suspensionType" => SuspensionType.ToString(),
        "aeroKit" => AeroKit.ToString(),
        _ => "",
    };

    /// <summary>Sets a categorical select from a legacy token string. Unknown/invalid tokens are
    /// ignored (the select keeps its current value), mirroring a browser select rejecting an
    /// unrecognized value. Does NOT notify — the caller raises <see cref="Changed"/> once.</summary>
    private void SetSelectSilent(string id, string token)
    {
        switch (id)
        {
            case "drivetrain": if (Enum.TryParse<Drivetrain>(token, out var dt)) Drivetrain = dt; break;
            case "engineLocation": if (Enum.TryParse<EngineLocation>(token, out var el)) EngineLocation = el; break;
            case "powertrain": if (Enum.TryParse<Powertrain>(token, out var pt)) Powertrain = pt; break;
            case "piClass": if (Enum.TryParse<PiClass>(token, out var pc)) PiClass = pc; break;
            case "tireCompound": if (Enum.TryParse<TireCompound>(token, out var tc)) TireCompound = tc; break;
            case "suspensionType": if (Enum.TryParse<SuspensionType>(token, out var st)) SuspensionType = st; break;
            case "aeroKit": if (Enum.TryParse<AeroKit>(token, out var ak)) AeroKit = ak; break;
        }
    }

    /// <summary>
    /// Build a snapshot of the full Car Setup panel as a <see cref="SavedSetup"/> ready for the store
    /// (legacy <c>snapshotSetup(name)</c>, app.js:584-598). Captures units, the current goal token,
    /// both dial values (as strings), and every categorical select + numeric field keyed by its
    /// legacy element id. <paramref name="name"/> is taken verbatim; the store trims it on validate.
    /// </summary>
    public SavedSetup SnapshotSetup(string name)
    {
        var fields = new JsonObject();
        // selects first (DOM order), then numeric fields (DOM order) — order is cosmetic since the
        // store keys by id, but matching legacy keeps exported JSON readable/diff-friendly.
        foreach (var id in SelectIds) fields[id] = GetSelect(id);
        foreach (var id in FieldIds) fields[id] = GetField(id);

        var dials = new JsonObject
        {
            ["handlingBias"] = FormatNumberLikeJs(HandlingBias),
            ["overallStiffness"] = FormatNumberLikeJs(OverallStiffness),
        };

        return SetupsStore.ValidateSetup(new JsonObject
        {
            ["name"] = name,
            ["savedAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            ["units"] = _unitSystem == UnitSystem.Metric ? "metric" : "imperial",
            ["goal"] = CurrentGoal.ToString(),
            ["dials"] = dials,
            ["fields"] = fields,
        })!; // ValidateSetup only returns null for a blank name / non-object fields — neither here.
    }

    /// <summary>
    /// Restore a saved setup into the live state (legacy <c>applySetup(s)</c>, app.js:600-615).
    /// Order matters and matches legacy exactly: <b>units first</b>, then overwrite every field/select
    /// from the setup's <c>fields</c> bag, then the two dials (absent dial keys reset to center "0"),
    /// then the goal (only if it's a known token). Raises <see cref="Changed"/> once at the end so
    /// the whole restore is a single re-render.
    /// </summary>
    public void ApplySetup(SavedSetup setup)
    {
        // Units first. Set directly (no value conversion): the saved field strings are already in the
        // setup's own units and are overwritten verbatim right after, so converting them would be
        // wrong. (Legacy calls setUnits, which converts the stale on-screen values that are then
        // discarded — the net effect is identical: end-state units = saved units, fields = saved.)
        _unitSystem = setup.Units == "metric" ? UnitSystem.Metric : UnitSystem.Imperial;

        // Overwrite each field/select that still exists, verbatim from the snapshot.
        foreach (var kvp in setup.Fields)
        {
            string id = kvp.Key;
            string value = NodeToRawString(kvp.Value);
            if (_rawForm.ContainsKey(id)) _rawForm[id] = value;
            else if (Array.IndexOf(SelectIds, id) >= 0) SetSelectSilent(id, value);
            // ids that no longer exist (removed in a newer schema) are silently ignored.
        }

        // Dials: absent keys reset to center so a hand-edited import loads deterministically.
        HandlingBias = ParseDial(setup.Dials, "handlingBias");
        OverallStiffness = ParseDial(setup.Dials, "overallStiffness");

        // Goal: only adopt a recognized token (legacy GOALS.includes(s.goal) guard).
        if (Enum.TryParse<Goal>(setup.Goal, out var goal) && Enum.IsDefined(goal))
            CurrentGoal = goal;

        Notify();
    }

    /// <summary>A dial value from the snapshot's <c>dials</c> bag: a parseable number wins; an
    /// absent / blank / non-numeric value resets the dial to center (0), matching legacy
    /// <c>s.dials.x != null ? s.dials.x : "0"</c> then the slider's numeric coercion.</summary>
    private static double ParseDial(JsonObject dials, string key)
    {
        if (dials.TryGetPropertyValue(key, out var node) && node is not null)
        {
            string s = NodeToRawString(node).Trim();
            if (s.Length > 0
                && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && !double.IsNaN(v) && !double.IsInfinity(v))
            {
                return v;
            }
        }
        return 0;
    }

    /// <summary>The raw string content of a stored field/dial value (legacy <c>String(value)</c>):
    /// a JSON string yields its own text (unquoted); any other scalar/shape yields its serialized
    /// form; a missing/null node yields "".</summary>
    private static string NodeToRawString(JsonNode? node)
    {
        if (node is null) return "";
        if (node is JsonValue v && v.TryGetValue(out string? s) && s is not null) return s;
        return node.ToString();
    }
}
