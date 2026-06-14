using System.Globalization;

namespace Fh6Tuning.Core;

/// <summary>
/// FH6 Tuning Engine — a 1:1 port of <c>legacy/tuning.js</c>.
///
/// <para><b>Byte-for-byte numeric parity</b> with the legacy JS engine is the hard contract:</para>
/// <list type="bullet">
///   <item>All math is <see cref="double"/> (IEEE-754, == JS Number); never decimal/float.</item>
///   <item>Rounding uses <see cref="JsMath"/> (Math.round = half toward +Infinity); never Math.Round.</item>
///   <item>Operation order, parenthesization, and per-call-site fallbacks match the JS exactly.</item>
///   <item>Signed rounded outputs pass through <see cref="JsMath.NormZero"/> (JS JSON.stringify(-0)=="0").</item>
///   <item>At HandlingBias==0 AND OverallStiffness==0 the dials are skipped — baseline returned unchanged.</item>
/// </list>
///
/// The math is pure; the type is registered for DI as a singleton via <see cref="ITuningEngine"/>.
/// </summary>
public sealed class TuningEngine : ITuningEngine
{
    // ---- GOALS (legacy/tuning.js:28) ----
    private static readonly Goal[] _goals =
        [Goal.Circuit, Goal.Drag, Goal.Drift, Goal.OffRoad, Goal.Rally, Goal.Touge];

    public IReadOnlyList<Goal> Goals => _goals;

    // ---- GOAL_META (legacy/tuning.js:29-36) ----
    private static readonly IReadOnlyDictionary<Goal, GoalMeta> _goalMeta = new Dictionary<Goal, GoalMeta>
    {
        [Goal.Circuit] = new("Circuit", "🏁", "Grip & balance on tarmac"),
        [Goal.Drag] = new("Drag", "🚦", "Straight-line launch & top speed"),
        [Goal.Drift] = new("Drift", "💨", "Controllable rear break-away"),
        [Goal.OffRoad] = new("Off-Road", "⛰️", "Soft, tall, compliant"),
        [Goal.Rally] = new("Rally", "🌲", "Mixed-surface compromise"),
        [Goal.Touge] = new("Touge", "🏔️", "Tight technical mountain runs"),
    };

    public IReadOnlyDictionary<Goal, GoalMeta> GoalMeta => _goalMeta;

    /// <summary>legacy <c>goalName(g)</c> (line 37) — GOAL_META label, else the token.</summary>
    private static string GoalName(Goal g) => _goalMeta.TryGetValue(g, out var m) ? m.Label : GoalToken(g);

    /// <summary>The JS string token for a goal (the enum member name).</summary>
    private static string GoalToken(Goal g) => g.ToString();

    public double? OverallTireDiameter(double? widthMm, double? aspectPct, double? rimIn) =>
        TuneInput.OverallTireDiameter(widthMm, aspectPct, rimIn);

    // ======================================================================
    // JS Number → string for the why text. JS `${num}` and String(num) use the
    // shortest round-trip representation; .NET's default double.ToString() (Core 3.0+)
    // is also shortest-round-trippable. For the JsMath-quantized values produced here
    // they agree. InvariantCulture pins the "." separator.
    // ======================================================================
    private static string S(double x) => x.ToString(CultureInfo.InvariantCulture);

    // ======================================================================
    // helpers (legacy/tuning.js:40-49)
    // ======================================================================
    private static double Clamp(double x, double lo, double hi) => JsMath.Clamp(x, lo, hi);
    private static double R1(double x) => JsMath.R1(x);
    private static double R2(double x) => JsMath.R2(x);
    private static double RHalf(double x) => JsMath.RHalf(x);
    private static double R5(double x) => JsMath.R5(x);
    private static double RInt(double x) => JsMath.RInt(x);
    private static double REven(double x) => JsMath.REven(x);

    // PI class index 0..7 (D..X; R sits between S2 and X) — legacy PI_INDEX (line 49)
    private static int PiIndex(PiClass c) => (int)c;

    // ======================================================================
    // DERIVED QUANTITIES (legacy/tuning.js:54-82)
    // ======================================================================
    private static Derived Derive(TuneInput i)
    {
        double frac = Clamp(i.FrontWeightPct / 100, 0.2, 0.8);
        double rearFrac = 1 - frac;
        double frontAxle = i.Weight * frac;
        double rearAxle = i.Weight * rearFrac;
        int piIdx = PiIndex(i.PiClass); // non-nullable enum: the `!= null ? : 3` fallback can't occur
        ClassTier classTier = piIdx <= 1 ? ClassTier.Sports : piIdx <= 3 ? ClassTier.HighPerf : ClassTier.Race;
        // tire grip factor: more grip tolerates/wants more negative camber, more diff lock, etc.
        double gripFactor = i.TireCompound switch
        {
            TireCompound.Race => 1.0,
            TireCompound.Drag => 0.9,
            // NOTE: legacy keys this off "Sport" (not the enum "Sport"? the JS table uses Race/Drag/
            // Sport/Rally/Street/Stock/Offroad). The enum member is `Sport`.
            TireCompound.Sport => 0.8,
            TireCompound.Rally => 0.6,
            TireCompound.Street => 0.55,
            TireCompound.Stock => 0.5,
            TireCompound.Offroad => 0.45,
            _ => 0.7,
        };
        double pw = i.Power / i.Weight;
        // Handling-tendency flags shared by aero/arb/differential.
        bool understeerProne = i.Drivetrain == Drivetrain.FWD || i.FrontWeightPct >= 53;
        bool oversteerProne = i.Drivetrain != Drivetrain.FWD &&
            (i.EngineLocation == EngineLocation.Rear || i.FrontWeightPct <= 46 ||
                (i.Drivetrain == Drivetrain.RWD && (i.FrontWeightPct <= 49 || pw >= 0.16)));
        return new Derived(
            Frac: frac, RearFrac: rearFrac,
            FrontAxle: frontAxle, RearAxle: rearAxle,
            FrontCorner: frontAxle / 2, RearCorner: rearAxle / 2,
            Pw: pw,
            PiIdx: piIdx, ClassTier: classTier, GripFactor: gripFactor,
            UndersteerProne: understeerProne, OversteerProne: oversteerProne,
            CanTuneSusp: i.SuspensionType != SuspensionType.Stock,
            EvFactor: i.Powertrain == Powertrain.EV ? 1 : i.Powertrain == Powertrain.Hybrid ? 0.5 : 0,
            IsEv: i.Powertrain == Powertrain.EV);
    }

    // ======================================================================
    // TIRES — front & rear cold psi (range 15-55) — legacy 87-127
    // ======================================================================
    private static Tires Tires(TuneInput i, Derived d, Goal goal)
    {
        double baseBaseline = i.TireCompound switch // BASE[compound] (line 88)
        {
            TireCompound.Street => 29.0,
            TireCompound.Sport => 29.5,
            TireCompound.Race => 30.0,
            TireCompound.Rally => 27.0,
            TireCompound.Drag => 30.0,
            TireCompound.Offroad => 24.0,
            TireCompound.Stock => 28.5,
            _ => double.NaN, // not reachable; BASE[key] != null ? : 29.0 handled below
        };
        // base = BASE[i.tireCompound] != null ? BASE[i.tireCompound] : 29.0
        double basePsi = !double.IsNaN(baseBaseline) ? baseBaseline : 29.0;
        double wAdj = Clamp((i.Weight - 3000) / 1323 * 1.0, -5, 5); // +1 psi / 600 kg
        basePsi += wAdj;
        if (d.PiIdx >= PiClassInfo.A) basePsi += 0.5; // high-perf class bonus

        double f = basePsi, r = basePsi;
        if (i.AeroInstalled) { f += 0.5; r += 0.5; }
        if (d.PiIdx >= PiClassInfo.S1) { f += 0.5; r += 0.5; } // wide sticky tire proxy

        // drivetrain split — { FWD: 1.5, RWD: 0.75, AWD: 0.35 }[dt] || 0.5
        double split = i.Drivetrain switch
        {
            Drivetrain.FWD => 1.5,
            Drivetrain.RWD => 0.75,
            Drivetrain.AWD => 0.35,
            _ => 0.5,
        };
        f += split / 2; r -= split / 2;

        // per-goal
        if (goal == Goal.Drag)
        {
            if (i.Drivetrain == Drivetrain.FWD) { f = basePsi - 8; r = basePsi + 5; }
            else { f = basePsi + 5; r = basePsi - 8; }
        }
        else if (goal == Goal.Drift) { f = 30; r = 27; }
        else if (goal == Goal.OffRoad) { f -= 2; r -= 2; }
        else if (goal == Goal.Rally) { f -= 1; r -= 1; }
        else if (goal == Goal.Touge) { f += 0.5; r += 0.5; }

        f = Clamp(RHalf(f), 15, 55); r = Clamp(RHalf(r), 15, 55);

        // why: BASE[i.tireCompound] || 29 — falsy fallback (BASE never 0, so equals base lookup or 29)
        double baseForWhy = !double.IsNaN(baseBaseline) && baseBaseline != 0 ? baseBaseline : 29;
        string text =
            $"Cold pressures target the grip window for {TireCompoundToken(i.TireCompound)} tires (~{S(baseForWhy)} psi baseline), {(wAdj >= 0 ? "raised" : "lowered")} {S(Math.Abs(R1(wAdj)))} for the {S(RInt(i.Weight))} lb mass. {DrivetrainToken(i.Drivetrain)} biases the front +{S(R1(split))} psi over rear for steering response." +
            (goal == Goal.Drag ? " Drag floods the driven axle with grip (big soft patch) and firms the free axle to cut rolling drag." : "") +
            (goal == Goal.Drift ? " Drift runs low overall (30/27) so the rear slides predictably." : "") +
            (goal == Goal.OffRoad || goal == Goal.Rally ? " Lowered further for a bigger, more compliant patch on loose ground." : "");
        string formula =
            $"psi = compoundBase + (weight−3000)/1323 + classBonus ± split/2\n{GoalToken(goal)} adjust; clamp 15–55, step 0.5";

        return new Tires(f, r, new Why(text, formula));
    }

    // ======================================================================
    // GEARING — final drive + ratios. FD∈[2,7], gear∈[0.5,5.5] — legacy 132-227
    // ======================================================================
    private static Gearing Gearing(TuneInput i, Derived d, Goal goal)
    {
        const double FD_MIN = 2.0, FD_MAX = 7.0, GEAR_MIN = 0.5, GEAR_MAX = 5.5;
        // GOAL_G[goal] = { fd, B }
        (double fd, double B) goalG = goal switch
        {
            Goal.Circuit => (0.0, -0.65),
            Goal.Drag => (-0.20, -0.58),
            Goal.Drift => (0.10, -0.70),
            Goal.OffRoad => (0.75, -0.72),
            Goal.Rally => (0.50, -0.70),
            Goal.Touge => (0.20, -0.66),
            _ => (0.0, -0.65),
        };

        // continuous FD anchored 400hp -> 4.25
        double fd = 4.25 + Clamp((400 - i.Power) / 600, -0.60, 0.60);
        fd += Clamp((i.Weight - 3000) / 500 * 0.10, -0.50, 0.50); // heavier -> shorter
        fd += goalG.fd;
        if (i.AeroInstalled && goal != Goal.Drag) fd -= 0.10; // drag caps top-gear pull
        if (i.Drivetrain == Drivetrain.AWD || i.Drivetrain == Drivetrain.FWD) fd += 0.05;
        if (i.EngineLocation == EngineLocation.Rear) fd -= 0.05;
        if (i.EngineLocation == EngineLocation.Mid) fd -= 0.03;
        if (d.IsEv) fd -= 0.15; // flat torque -> longer single ratio
        fd = Clamp(R2(fd), FD_MIN, FD_MAX);

        // EV single-speed (lines 158-182)
        if (d.IsEv)
        {
            double evRatio = R2(Clamp(1.20 + (1 - d.Pw / 0.40) * 0.30, 0.90, 1.60));
            const double EV_HEADROOM = 1.07;
            double RL = i.RedlineRpm ?? 0, TD = i.TireDiameter ?? 0, TT = i.TargetTopSpeed ?? 0;
            bool canSpeed = RL > 0 && TD > 0;
            string fdSource = "heuristic";
            if (canSpeed && TT > 0)
            {
                fd = Clamp(R2((RL * Math.PI * TD * 60) / (63360 * TT * EV_HEADROOM * evRatio)), FD_MIN, FD_MAX);
                fdSource = "target";
            }
            IReadOnlyList<double>? speeds = canSpeed
                ? [RL / (evRatio * fd) * Math.PI * TD * 60 / 63360]
                : null;
            string evText =
                (fdSource == "target"
                    ? $"This EV is single-speed: set BOTH in-game sliders — final drive {S(fd)} and the lone \"1st\" ratio {S(evRatio)} (sized to this car's {S(R2(d.Pw))} hp/lb); the final drive is only correct paired with that 1st value. The pair is back-solved so the {S(RL)} rpm limiter arrives ~7% past your target: FH6 EV motors lose power near redline, so gearing the limiter exactly at the target tops out short. Expect the in-game gearing graph to max at the \"@ redline\" speed shown, and the simulated top speed to land on your target. "
                    : $"This EV is single-speed: set BOTH in-game sliders — final drive {S(fd)} and the lone \"1st\" ratio {S(evRatio)}. That ratio is tuned to this car's {S(R2(d.Pw))} hp/lb (taller for stronger cars) and the final drive runs slightly long because flat instant torque has no power band to keep. ") +
                (canSpeed && fdSource != "target" ? "Top speed is computed from your redline and tire diameter." : "");
            string evFormula =
                fdSource == "target"
                    ? "evRatio = clamp(1.20 + (1 − pw/0.40)×0.30, 0.90, 1.60)\nFD = redline × π × tireØ × 60 / (63360 × targetMph × 1.07 × evRatio)\n1.07 = rpm headroom (FH6 EV power falls off near redline)"
                    : "evRatio = clamp(1.20 + (1 − pw/0.40)×0.30, 0.90, 1.60)\nFD = 4.25 + clamp((400−hp)/600, ±0.6) + weight&goal adj − 0.15 (EV)";
            return new Gearing(
                Final: fd, Ratios: [evRatio], SingleSpeed: true,
                Speeds: speeds, TopSpeed: speeds != null ? speeds[0] : null, FdSource: fdSource,
                Why: new Why(evText, evFormula));
        }

        // first gear from power-to-weight, nudged by goal
        double A = 3.40 - Clamp((d.Pw - 0.05) / 0.35, 0, 1) * 1.0; // 3.40 → 2.40
        double aGoalAdj = goal switch
        {
            Goal.Circuit => 0,
            Goal.Drag => 0.30,
            Goal.Drift => -0.10,
            Goal.OffRoad => 0.40,
            Goal.Rally => 0.30,
            Goal.Touge => 0.10,
            _ => 0,
        };
        A += aGoalAdj;
        A = Clamp(A, 1.80, 5.50);
        double B = goalG.B;
        if (i.Powertrain == Powertrain.Hybrid) B -= 0.02;
        if (i.Drivetrain == Drivetrain.AWD && (goal == Goal.Rally || goal == Goal.OffRoad)) B -= 0.02;

        int N = (int)Clamp(JsMath.Round(i.Gears), 2, 10);
        List<double> ratios = [];
        for (int n = 1; n <= N; n++) ratios.Add(Clamp(A * Math.Pow(n, B), GEAR_MIN, GEAR_MAX));
        // enforce strictly descending
        for (int k = 1; k < ratios.Count; k++)
            if (ratios[k] >= ratios[k - 1] - 0.01) ratios[k] = ratios[k - 1] - 0.05;
        double lo = ratios.Min();
        if (lo < GEAR_MIN) { double add = GEAR_MIN - lo; ratios = ratios.Select(x => x + add).ToList(); }
        ratios = ratios.Select(x => R2(Clamp(x, GEAR_MIN, GEAR_MAX))).ToList();

        // optional physics
        double RL2 = i.RedlineRpm ?? 0, TD2 = i.TireDiameter ?? 0, TT2 = i.TargetTopSpeed ?? 0;
        bool canSpeed2 = RL2 > 0 && TD2 > 0;
        double topRatio = ratios[ratios.Count - 1];
        string fdSource2 = "heuristic";
        if (canSpeed2 && TT2 > 0 && topRatio > 0)
        {
            fd = Clamp(R2((RL2 * Math.PI * TD2 * 60) / (63360 * TT2 * topRatio)), FD_MIN, FD_MAX);
            fdSource2 = "target";
        }
        IReadOnlyList<double>? speeds2 = canSpeed2
            ? ratios.Select(gr => RL2 / (gr * fd) * Math.PI * TD2 * 60 / 63360).ToList()
            : null;
        double? topSpeed = speeds2 != null ? speeds2[speeds2.Count - 1] : null;

        string text =
            (fdSource2 == "target"
                ? $"Final drive is back-solved from physics so top gear ({S(R2(topRatio))}) just reaches your target top speed at the {S(RL2)} rpm redline — more exact than the power heuristic. "
                : $"Final drive uses the community formula anchored at 400 hp → 4.25, shifted for this car's {S(i.Power)} hp and {S(RInt(i.Weight))} lb, then {(goalG.fd >= 0 ? "+" : "")}{S(goalG.fd)} for {GoalName(goal)}. ") +
            $"Gears follow Rₙ = A·nᴮ with 1st = {S(R2(A))} (from {S(R2(d.Pw))} hp/lb) and spacing exponent B = {S(B)} — wide low gears tame wheelspin, tight top gears stay in the power band." +
            (canSpeed2 ? $" Per-gear and top speeds are computed from your {S(RL2)} rpm redline and tire diameter." : "");
        string formula =
            (fdSource2 == "target"
                ? "FD = redline × π × tireØ × 60 / (63360 × targetMph × topGear)"
                : "FD = 4.25 + clamp((400−hp)/600, ±0.6) + weightAdj + goalAdj") +
            $"\nRₙ = {S(R2(A))} × n^{S(B)}" + (canSpeed2 ? "\nspeed = redline / (gear × FD) × π × tireØ × 60/63360" : "");

        return new Gearing(fd, ratios, false, speeds2, topSpeed, fdSource2, new Why(text, formula));
    }

    // ======================================================================
    // ALIGNMENT — camber, toe, caster — legacy 232-293
    // ======================================================================
    private static Alignment Alignment(TuneInput i, Derived d, Goal goal)
    {
        if (!d.CanTuneSusp)
        {
            return new Alignment(0, 0, 0, 0, 5.0, new Why(
                "Stock suspension locks alignment at the factory setting — install an upgraded suspension to tune camber, toe and caster.",
                "stock = locked"));
        }
        double g = d.GripFactor;
        // front camber from grip, drivetrain, front load
        double camF = -(0.6 + (g - 0.45) / 0.55 * (2.0 - 0.6));
        camF += i.Drivetrain switch { Drivetrain.RWD => -0.3, Drivetrain.AWD => 0.0, Drivetrain.FWD => 0.3, _ => 0 };
        double effFw = i.FrontWeightPct + (i.EngineLocation == EngineLocation.Rear ? -3 : i.EngineLocation == EngineLocation.Mid ? -1.5 : 0);
        camF += -0.1 * (effFw - 50) / 4;
        camF = goal switch
        {
            Goal.Circuit => camF - 0.2,
            Goal.Drag => -0.3,
            Goal.Drift => Clamp(-3.0 - g * 2.0, -5.0, -3.0),
            Goal.OffRoad => -0.5,
            Goal.Rally => Clamp(-0.8 - g * 0.4, -1.2, -0.8),
            Goal.Touge => camF + 0.1,
            _ => camF,
        };
        camF = Clamp(R1(camF), -5, 0);

        // rear camber ~ half of front
        double camR = camF * 0.55 + (i.Drivetrain switch { Drivetrain.RWD => 0.2, Drivetrain.AWD => 0, Drivetrain.FWD => -0.2, _ => 0 }) - 0.1 * (d.RearFrac * 100 - 50) / 6;
        camR = goal switch
        {
            Goal.Circuit => Clamp(camR, -1.0, -0.5),
            Goal.Drag => -0.2,
            Goal.Drift => -1.0,
            Goal.OffRoad => -0.5,
            Goal.Rally => Clamp(-0.5 - g * 0.3, -0.8, -0.5),
            Goal.Touge => Clamp(camR + 0.05, -1.0, -0.4),
            _ => camR,
        };
        camR = Clamp(R1(camR), -5, 0);
        if (i.EngineLocation != EngineLocation.Front && goal != Goal.OffRoad) camR = Clamp(R1(camR - 0.2), -5, 0);

        // front toe (− = toe-out)
        bool understeerProne = i.Drivetrain == Drivetrain.FWD || effFw >= 55;
        double toeF = goal switch
        {
            Goal.Circuit => understeerProne ? -0.1 : -0.05,
            Goal.Drag => 0.0,
            Goal.Drift => -0.2,
            Goal.OffRoad => -0.2,
            Goal.Rally => -0.1,
            Goal.Touge => -0.1,
            _ => 0.0,
        };
        toeF = Clamp(R1(toeF), -5, 5);

        // rear toe (+ = toe-in)
        double toeR = goal switch
        {
            Goal.Circuit => i.Drivetrain == Drivetrain.RWD ? 0.1 : 0.0,
            Goal.Drag => 0.1,
            Goal.Drift => -0.1,
            Goal.OffRoad => 0.1,
            Goal.Rally => 0.1,
            Goal.Touge => i.Drivetrain == Drivetrain.RWD ? 0.2 : 0.1,
            _ => 0.0,
        };
        if (i.Drivetrain == Drivetrain.RWD && i.Torque >= 400 && goal != Goal.Drift) toeR += 0.1;
        toeR = Clamp(R1(toeR), -5, 5);

        // caster from weight + class + aero
        double caster = 5.0 + Clamp((i.Weight - 2400) / 1800, 0, 1) * 2.0;
        // { D:-0.3, C:-0.1, B:0, A:0.1, S1:0.3, S2:0.4, R:0.45, X:0.5 }[piClass] ?? 0
        caster += i.PiClass switch
        {
            PiClass.D => -0.3,
            PiClass.C => -0.1,
            PiClass.B => 0,
            PiClass.A => 0.1,
            PiClass.S1 => 0.3,
            PiClass.S2 => 0.4,
            PiClass.R => 0.45,
            PiClass.X => 0.5,
            _ => 0,
        };
        if (i.AeroInstalled) caster += 0.2;
        caster = goal switch
        {
            Goal.Circuit => caster,
            Goal.Drag => 5.0,
            Goal.Drift => caster + 1.0,
            Goal.OffRoad => caster - 0.5,
            Goal.Rally => caster - 0.3,
            Goal.Touge => caster + 0.2,
            _ => caster,
        };
        caster = Clamp(R1(caster), 1, 7);
        if (goal == Goal.Circuit || goal == Goal.Touge || goal == Goal.Drift) caster = Clamp(caster, 5, 7);

        // NormZero on signed outputs (toe especially can emit -0; camber too)
        camF = JsMath.NormZero(camF);
        camR = JsMath.NormZero(camR);
        toeF = JsMath.NormZero(toeF);
        toeR = JsMath.NormZero(toeR);

        string text =
            $"Front camber comes from the {TireCompoundToken(i.TireCompound)} grip factor ({S(g)}), {DrivetrainToken(i.Drivetrain)} bias and front load → {S(camF)}°; rear runs ~55% of that ({S(camR)}°). {GoalName(goal)} {(goal == Goal.Drift ? "pushes front camber to the limit for big steering angles and adds front toe-out for counter-steer" : "keeps toe near zero to avoid scrub")}. Caster {S(caster)}° scales with the {S(RInt(i.Weight))} lb mass and {PiClassToken(i.PiClass)}-class speed for self-centring.";
        string formula =
            $"camberF = −(0.6 + (grip−0.45)/0.55 × 1.4) + dtBias + loadTrim → {GoalName(goal)}\ncaster = 5.0 + clamp((wt−2400)/1800)×2 + classBump";

        return new Alignment(camF, camR, toeF, toeR, caster, new Why(text, formula));
    }

    // ======================================================================
    // ANTI-ROLL BARS (range 1-65) — legacy 298-328
    // ======================================================================
    private static Arb Arb(TuneInput i, Derived d, Goal goal)
    {
        if (!d.CanTuneSusp)
            return new Arb(32.5, 32.5, new Why(
                "Stock suspension can't meaningfully tune anti-roll bars — values centred. Upgrade the suspension to balance roll stiffness.",
                "stock = centred"));
        // { D:0.63, C:0.63, B:0.55, A:0.50, S1:0.45, S2:0.42, R:0.41, X:0.40 }[piClass] ?? 0.5
        double stiffPct = i.PiClass switch
        {
            PiClass.D => 0.63,
            PiClass.C => 0.63,
            PiClass.B => 0.55,
            PiClass.A => 0.50,
            PiClass.S1 => 0.45,
            PiClass.S2 => 0.42,
            PiClass.R => 0.41,
            PiClass.X => 0.40,
            _ => 0.5,
        };
        double basev = (i.Weight / 2) / (200 - 200 * stiffPct); // ForzaFire base
        // { RWD:1.0, AWD:0.66, FWD:-1.0 }[dt] — no fallback in JS (undefined if other)
        double splitPer1 = i.Drivetrain switch { Drivetrain.RWD => 1.0, Drivetrain.AWD => 0.66, Drivetrain.FWD => -1.0, _ => double.NaN };
        double splitDelta = splitPer1 * (i.FrontWeightPct - 50);
        double front = basev + splitDelta / 2;
        double rear = basev - splitDelta / 2;

        // gm = { Circuit:[1,1], Drag:[0.40,0.55], Drift:[0.45,1.45], OffRoad:[0.30,0.30], Rally:[0.55,0.55], Touge:[1.05,0.95] }[goal]
        (double g0, double g1) gm = goal switch
        {
            Goal.Circuit => (1, 1),
            Goal.Drag => (0.40, 0.55),
            Goal.Drift => (0.45, 1.45),
            Goal.OffRoad => (0.30, 0.30),
            Goal.Rally => (0.55, 0.55),
            Goal.Touge => (1.05, 0.95),
            _ => (1, 1),
        };
        front *= gm.g0; rear *= gm.g1;

        if (i.Drivetrain == Drivetrain.FWD && goal != Goal.Drift) front = Math.Min(front, basev * 0.85);
        if (i.Drivetrain == Drivetrain.AWD && (goal == Goal.Circuit || goal == Goal.Touge)) { front *= 0.92; rear *= 1.08; }
        if (i.EngineLocation != EngineLocation.Front && goal != Goal.Drift) rear *= 0.92;
        if (i.AeroInstalled && (goal == Goal.Circuit || goal == Goal.Touge)) { front *= 1.08; rear *= 1.08; }
        if (d.IsEv) { front *= 1.05; rear *= 1.05; }
        if (d.OversteerProne && goal != Goal.Drift) { front *= 1.06; rear *= 0.80; }

        front = Clamp(R2(front), 1, 65); rear = Clamp(R2(rear), 1, 65);

        string text =
            $"Base bar from the ForzaFire formula: (½ weight) ÷ (200 − 200 × {S(stiffPct)} class-stiffness) = {S(R1(basev))}, split {(i.Drivetrain == Drivetrain.FWD ? "softer front" : "stiffer front")} by the {S(R1(i.FrontWeightPct))}% weight bias. {GoalName(goal)} then scales front ×{S(gm.g0)} / rear ×{S(gm.g1)}" +
            (goal == Goal.Drift ? " — a soft front + very stiff rear provokes rotation on demand." : goal == Goal.OffRoad ? " — very soft both ends so wheels stay loaded over terrain." : ".");
        string formula =
            $"base = (weight/2) / (200 − 200·stiff%)\nfront = base + split/2 ;  rear = base − split/2\n× {GoalName(goal)} [{S(gm.g0)}, {S(gm.g1)}] ; clamp 1–65";

        return new Arb(front, rear, new Why(text, formula));
    }

    // ======================================================================
    // SPRINGS + RIDE HEIGHT (ride-frequency model) — legacy 333-401
    // Returns (Springs, fFront, fRear) — the frequency scratch is for the damping
    // handoff + the why string only; NOT serialized.
    // ======================================================================
    private static (Springs springs, double fFront, double fRear) Springs(TuneInput i, Derived d, Goal goal)
    {
        double srMinF = i.SpringRateMinF ?? i.SpringRateMin ?? double.NaN;
        double srMaxF = i.SpringRateMaxF ?? i.SpringRateMax ?? double.NaN;
        double srMinR = i.SpringRateMinR ?? i.SpringRateMin ?? double.NaN;
        double srMaxR = i.SpringRateMaxR ?? i.SpringRateMax ?? double.NaN;
        double rhMinF = i.RideHeightMinF ?? i.RideHeightMin ?? double.NaN;
        double rhMaxF = i.RideHeightMaxF ?? i.RideHeightMax ?? double.NaN;
        double rhMinR = i.RideHeightMinR ?? i.RideHeightMin ?? double.NaN;
        double rhMaxR = i.RideHeightMaxR ?? i.RideHeightMax ?? double.NaN;

        // FREQ_BASE[classTier]
        double freqBase = d.ClassTier switch { ClassTier.Sports => 1.9, ClassTier.HighPerf => 2.2, ClassTier.Race => 2.5, _ => double.NaN };
        // SUSP_CAP[suspensionType] || 5.0
        double suspCapLookup = i.SuspensionType switch
        {
            SuspensionType.Stock => 1.6,
            SuspensionType.Street => 2.2,
            SuspensionType.Sport => 2.8,
            SuspensionType.Race => 5.0,
            SuspensionType.Drift => 3.2,
            SuspensionType.Offroad => 2.0,
            _ => double.NaN,
        };
        double suspCap = !double.IsNaN(suspCapLookup) && suspCapLookup != 0 ? suspCapLookup : 5.0;

        double fFront = freqBase, fRear = freqBase;
        if (i.AeroInstalled) { fFront += 0.6; fRear += 0.6; }

        // G[goal]
        bool hasOverride = goal == Goal.OffRoad;
        if (hasOverride)
        {
            fFront = 1.1; fRear = 1.0;
        }
        else
        {
            (double fOff0, double fOff1, double mul0, double mul1) gg = goal switch
            {
                Goal.Circuit => (0.0, -0.1, 1, 1),
                Goal.Drag => (-0.6, 0.2, 1, 1),
                Goal.Drift => (-0.3, 0.4, 1, 1),
                Goal.Rally => (0, 0, 0.5, 0.5),
                Goal.Touge => (-0.1, -0.2, 1, 1),
                _ => (0.0, 0.0, 1, 1),
            };
            fFront = fFront * gg.mul0 + gg.fOff0;
            fRear = fRear * gg.mul1 + gg.fOff1;
        }
        fFront = Clamp(fFront, 0.8, suspCap); fRear = Clamp(fRear, 0.8, suspCap);

        // drivetrain split — { FWD:[-0.10,0.10], AWD:[0.05,0.05], RWD:[0.05,-0.05] }[dt] || [0,0]
        (double s0, double s1) sp = i.Drivetrain switch
        {
            Drivetrain.FWD => (-0.10, 0.10),
            Drivetrain.AWD => (0.05, 0.05),
            Drivetrain.RWD => (0.05, -0.05),
            _ => (0, 0),
        };
        fFront = Clamp(fFront + sp.s0, 0.8, suspCap); fRear = Clamp(fRear + sp.s1, 0.8, suspCap);

        // K = f²·Wcorner / 9.78
        double front = (fFront * fFront * d.FrontCorner) / 9.78;
        double rear = (fRear * fRear * d.RearCorner) / 9.78;
        double devPts = i.FrontWeightPct - 50;
        double dirn = i.Drivetrain == Drivetrain.FWD ? -1 : 1;
        front += 15 * Math.Max(0, devPts) * dirn * 0.5;
        rear -= 15 * Math.Max(0, devPts) * dirn * 0.5;
        if (i.Powertrain == Powertrain.EV) { front *= 1.08; rear *= 1.08; }
        if (i.Powertrain == Powertrain.Hybrid) { front *= 1.04; rear *= 1.04; }

        front = Clamp(front, srMinF, srMaxF); rear = Clamp(rear, srMinR, srMaxR);
        // Physics floor
        string rangeNote = "";
        double support = front * 2 * rhMinF + rear * 2 * rhMinR;
        if (support < i.Weight)
        {
            double scale = i.Weight / support;
            front = Clamp(front * scale, srMinF, srMaxF); rear = Clamp(rear * scale, srMinR, srMaxR);
            rangeNote = " (raised to the support floor for this weight)";
        }
        front = RInt(front); rear = RInt(rear);

        // RIDE HEIGHT — fraction of each axle's own part range per goal + modifiers
        (double pF, double pR) ph = goal switch
        {
            Goal.Circuit => (0, 0),
            Goal.Drag => (0, 0.30),
            Goal.Drift => (0.05, 0.05),
            Goal.OffRoad => (1, 1),
            Goal.Rally => (0.75, 0.75),
            Goal.Touge => (0.05, 0.05),
            _ => (0, 0),
        };
        double pF = ph.pF, pR = ph.pR;
        // { Rally:0.10, Offroad:0.15 }[tireCompound] || 0
        double rhComp = i.TireCompound switch { TireCompound.Rally => 0.10, TireCompound.Offroad => 0.15, _ => 0 };
        pF = Clamp(pF + rhComp, 0, 1); pR = Clamp(pR + rhComp, 0, 1);
        if (i.AeroInstalled && (goal == Goal.Circuit || goal == Goal.Touge || goal == Goal.Drag)) pR = Clamp(pR + 0.05, 0, 1);
        bool softFront = (front - srMinF) < 0.25 * (srMaxF - srMinF);
        if (softFront && pF < 0.15) { pF = Clamp(pF + 0.10, 0, 1); pR = Clamp(pR + 0.10, 0, 1); }
        if (i.Weight > 4000) { pF = Clamp(pF + 0.05, 0, 1); pR = Clamp(pR + 0.05, 0, 1); }
        double rideF = R1(Clamp(rhMinF + (rhMaxF - rhMinF) * pF, rhMinF, rhMaxF));
        double rideR = R1(Clamp(rhMinR + (rhMaxR - rhMinR) * pR, rhMinR, rhMaxR));

        string text =
            $"Spring rate is solved from a target ride frequency ({S(R1(fFront))} Hz front / {S(R1(fRear))} Hz rear for {ClassTierToken(d.ClassTier)} class{(i.AeroInstalled ? " + aero" : "")}, {GoalName(goal)}) against the {S(RInt(d.FrontCorner))} lb on each front corner: K = f²·W/9.78{rangeNote}. Ride height sits at {S(JsMath.Round(pF * 100))}% of travel — {(pF < 0.2 ? "as low as practical for a low CoG" : pF > 0.8 ? "high for clearance" : "raised for compliance")}{(goal == Goal.Drag ? ", with forward rake (higher rear) to plant the drive wheels" : "")}.";
        string formula =
            $"K = f² × Wcorner / 9.78   (f from class+aero+{GoalName(goal)})\nride = rhMin + (rhMax−rhMin) × goalFrac";

        return (new Springs(front, rear, rideF, rideR, new Why(text, formula)), fFront, fRear);
    }

    // ======================================================================
    // DAMPING — rebound & bump (1-20) — legacy 406-444
    // ======================================================================
    private static Damping Damping(TuneInput i, Derived d, Goal goal, double sprFront, double sprRear)
    {
        double minBump = d.ClassTier switch { ClassTier.Sports => 4.6, ClassTier.HighPerf => 4.7, ClassTier.Race => 4.8, _ => double.NaN };
        double fBump = minBump + (d.FrontAxle / 200) * 0.1;
        double fReb = fBump / 0.6;

        double diffPct = Math.Abs(sprFront - sprRear) / Math.Max(Math.Max(sprFront, sprRear), 1) * 100;
        (double reb, double bump) off = diffPct <= 1.5 ? (-0.2, -0.1) : diffPct <= 35 ? (-0.3, -0.2) : diffPct <= 40 ? (-0.6, -0.4) : (-1.2, -0.8);
        double rBump = fBump + off.bump;
        double rReb = fReb + off.reb;

        // dg = { Circuit:[1,1,0], Drag:[1.10,1.05,0], Drift:[0.55,0.55,0], OffRoad:[0.30,0.45,1.0], Rally:[0.55,0.65,1.0], Touge:[0.85,0.90,0] }[goal]
        (double d0, double d1, double d2) dg = goal switch
        {
            Goal.Circuit => (1, 1, 0),
            Goal.Drag => (1.10, 1.05, 0),
            Goal.Drift => (0.55, 0.55, 0),
            Goal.OffRoad => (0.30, 0.45, 1.0),
            Goal.Rally => (0.55, 0.65, 1.0),
            Goal.Touge => (0.85, 0.90, 0),
            _ => (1, 1, 0),
        };
        fBump *= dg.d0; rBump *= dg.d0;
        fReb = fReb * dg.d1 + dg.d2; rReb = rReb * dg.d1 + dg.d2;

        if (i.Drivetrain == Drivetrain.FWD) { fBump += 0.5; fReb += 0.5; rBump -= 0.3; rReb -= 0.3; }
        else if (i.Drivetrain == Drivetrain.RWD) { rBump += 0.3; rReb += 0.3; }
        else { fBump += 0.2; fReb += 0.2; rBump += 0.2; rReb += 0.2; }

        if (goal == Goal.Drag && i.Drivetrain == Drivetrain.RWD) { fReb = Math.Max(fReb - 2.0, 1.0); fBump += 1.0; rReb += 1.0; }
        if (i.EngineLocation != EngineLocation.Front) { rBump += 0.4; rReb += 0.4; }
        if (i.Powertrain == Powertrain.EV) { fReb += 0.3; rReb += 0.3; }
        if (goal == Goal.OffRoad || goal == Goal.Rally || i.SuspensionType == SuspensionType.Offroad) { fBump = Math.Max(1.0, fBump); rBump = Math.Max(1.0, rBump); }

        fBump = Clamp(fBump, 1, 20); rBump = Clamp(rBump, 1, 20);
        fReb = Clamp(fReb, 1, 20); rReb = Clamp(rReb, 1, 20);
        bool bypass = (goal == Goal.Drag && i.Drivetrain == Drivetrain.RWD) || goal == Goal.OffRoad || goal == Goal.Rally;
        if (!bypass) { fBump = Clamp(fBump, 0.4 * fReb, 0.7 * fReb); rBump = Clamp(rBump, 0.4 * rReb, 0.7 * rReb); }

        string text =
            $"Bump is derived from the {S(RInt(d.FrontAxle))} lb front axle (MinBump {S(minBump)} + axle/200×0.1) and rebound set at ~60% above it. {GoalName(goal)} scales bump ×{S(dg.d0)} / rebound ×{S(dg.d1)}{(dg.d2 != 0 ? $" +{S(dg.d2)} rebound" : "")}" +
            (goal == Goal.OffRoad || goal == Goal.Rally ? " for terrain compliance and recovery." : $" for a {(dg.d0 < 0.7 ? "compliant" : "stable")} platform.") +
            $" {DrivetrainToken(i.Drivetrain)} biases damping toward the {(i.Drivetrain == Drivetrain.FWD ? "front" : "rear")}.";
        string formula =
            $"bump = MinBump(class) + frontAxle/200 × 0.1\nrebound = bump / 0.6  →  × {GoalName(goal)} mults ; clamp 1–20";

        return new Damping(R1(fReb), R1(rReb), R1(fBump), R1(rBump), new Why(text, formula));
    }

    // ======================================================================
    // AERO — front & rear downforce as % of each wing's slider range — legacy 449-538
    // ======================================================================
    private static Aero Aero(TuneInput i, Derived d, Goal goal)
    {
        bool hasF = i.HasFrontAero, hasR = i.HasRearAero;
        if (!hasF && !hasR)
        {
            return new Aero(false, null, null, null, null, new Why(
                "No adjustable aero is installed, so there's nothing to set here — grip comes from the body itself.",
                "—"));
        }
        // LEVEL[goal]
        double level = goal switch
        {
            Goal.Circuit => 0.85,
            Goal.Touge => 0.55,
            Goal.Drift => 0.30,
            Goal.Rally => 0.15,
            Goal.OffRoad => 0.05,
            Goal.Drag => 0.0,
            _ => double.NaN,
        };

        AeroRange fR = i.AeroFront; // [min,max] or [null,null]
        AeroRange rR = i.AeroRear;
        // toLbf(frac, rng): hasRange ? round(min + (max-min)*clamp(frac,0,1)) : null
        double? ToLbf(double frac, AeroRange rng) => rng.HasRange
            ? JsMath.Round(rng.Min!.Value + (rng.Max!.Value - rng.Min!.Value) * Clamp(frac, 0, 1))
            : (double?)null;
        bool HasRange(AeroRange rng) => rng.HasRange;
        const string lbfNote = " The % is mapped into the downforce range you entered to give the lbf shown.";
        const string lbfFormula = "\nlbf = min + (max − min) × fraction";

        bool understeerProne = d.UndersteerProne, oversteerProne = d.OversteerProne;

        if (hasF && !hasR) // front splitter only
        {
            double fac = oversteerProne ? 0.6 : understeerProne ? 1.0 : 0.85;
            double f = goal == Goal.Drag ? 0 : Clamp(level * fac, 0, 1);
            double fp = R5(f * 100);
            string fText =
                $"Front splitter only — there's no rear wing to balance against. {GoalName(goal)} runs {S(fp)}% of the splitter's range" +
                (goal == Goal.Drag ? ", floored — any front downforce is drag here." : oversteerProne ? $", kept moderate: on a tail-happy {DrivetrainToken(i.Drivetrain)} car a big splitter lightens the rear at speed and invites high-speed oversteer you can't dial out with a rear wing." : understeerProne ? ", used fully — front downforce directly fights this car's understeer." : ".") + (HasRange(fR) ? lbfNote : "");
            string fFormula =
                $"front = level({S(level)}) × balanceFactor({S(fac)})\nrear = n/a (no wing installed)" + (HasRange(fR) ? lbfFormula : "");
            return new Aero(true, fp, ToLbf(f, fR), null, null, new Why(fText, fFormula));
        }
        if (!hasF && hasR) // rear wing only
        {
            double fac = understeerProne ? 0.55 : oversteerProne ? 1.0 : 0.8;
            double r = goal == Goal.Drag ? 0 : Clamp(level * fac, 0, 1);
            if (i.EngineLocation == EngineLocation.Rear) r = Clamp(r - 0.10, 0, 1);
            if (i.EngineLocation == EngineLocation.Mid) r = Clamp(r - 0.05, 0, 1);
            double rp = R5(r * 100);
            string rText =
                $"Rear wing only — there's no front splitter to balance against. {GoalName(goal)} runs {S(rp)}% of the wing's range" +
                (goal == Goal.Drag ? ", floored — the wing is pure drag on a straight." : understeerProne ? ", kept low: this car already pushes and you can't add front downforce to offset it, so a big wing would only deepen high-speed understeer." : oversteerProne ? $", used fully — rear downforce calms a loose {DrivetrainToken(i.Drivetrain)} rear at speed." : ".") + (HasRange(rR) ? lbfNote : "");
            string rFormula =
                $"rear = level({S(level)}) × balanceFactor({S(fac)})\nfront = n/a (no splitter installed)" + (HasRange(rR) ? lbfFormula : "");
            return new Aero(true, null, null, rp, ToLbf(r, rR), new Why(rText, rFormula));
        }

        // full kit (both ends) — BALANCED-MAGNITUDE model anchored at a 47% front-weight ideal.
        // Aero balance comes from WEIGHT DISTRIBUTION (QuickTune-canonical), not a drivetrain slider
        // rule: front sits at the goal LEVEL of its range; rear targets the SAME downforce MAGNITUDE
        // (balanced at 47%), trimmed +1.867 lbf per 1% of front-weight above 47%. No AWD slider
        // override, no engine-location rear-shed; aero no longer reads the over/understeer flags.
        // Rear-engine cars are safeguarded: never below 0.50 front-share (rear lbf ≥ front lbf).
        double front, rear; // slider fractions 0..1
        if (goal == Goal.Drag)
        {
            front = 0; rear = 0;
        }
        else if (fR.HasRange && rR.HasRange)
        {
            double fSpan = fR.Max!.Value - fR.Min!.Value;
            double rSpan = rR.Max!.Value - rR.Min!.Value;
            double frontDF = fR.Min.Value + fSpan * level;
            if (i.Power >= 600 && goal != Goal.OffRoad)
                frontDF *= 1 + Math.Min((i.Power - 600) / 600, 0.5) * 0.5;
            frontDF = Clamp(frontDF, fR.Min.Value, fR.Max.Value);          // clamp front first
            double rearDF = frontDF + (i.FrontWeightPct - 47) * 1.867;     // balance to (clamped) front
            if (i.EngineLocation == EngineLocation.Rear) rearDF = Math.Max(rearDF, frontDF); // safeguard
            rearDF = Clamp(rearDF, rR.Min.Value, rR.Max.Value);
            front = fSpan > 0 ? (frontDF - fR.Min.Value) / fSpan : 0;
            rear = rSpan > 0 ? (rearDF - rR.Min.Value) / rSpan : 0;
        }
        else
        {
            // ranges unknown: fraction-space balance (front ≈ rear fraction), weight-trim via 250-lbf span.
            double frontF = level, rearF = level;
            if (i.Power >= 600 && goal != Goal.OffRoad)
            {
                double k = 1 + Math.Min((i.Power - 600) / 600, 0.5) * 0.5;
                frontF *= k; rearF *= k;
            }
            rearF += (i.FrontWeightPct - 47) * 1.867 / 250;
            if (i.EngineLocation == EngineLocation.Rear) rearF = Math.Max(rearF, frontF);
            front = Clamp(frontF, 0, 1);
            rear = Clamp(rearF, 0, 1);
        }

        double fp2 = R5(front * 100), rp2 = R5(rear * 100);
        double? frontLbf = ToLbf(front, fR);
        double? rearLbf = ToLbf(rear, rR);
        // share: front's portion of total ACTUAL downforce when lbf known, else from %.
        double share = (frontLbf != null && rearLbf != null && frontLbf + rearLbf > 0)
            ? JsMath.Round(frontLbf.Value / (frontLbf.Value + rearLbf.Value) * 100)
            : (fp2 + rp2 > 0 ? JsMath.Round(fp2 / (fp2 + rp2) * 100) : 50);

        string balancePart = goal != Goal.Drag ? $" (aero balance ≈ {S(share)}% front)" : "";
        string text =
            $"Downforce trades top speed for grip. {GoalName(goal)} runs {S(fp2)}% front / {S(rp2)}% of each wing's range{balancePart}" +
            (goal == Goal.Circuit ? ", near-max grip with rear sized to match front downforce so the car stays balanced at speed"
             : goal == Goal.Drag ? ", floored to zero — every pound of downforce is drag that kills top speed."
             : goal == Goal.Drift ? ", low so the car stays loose and easy to swing."
             : ", a moderate amount for the surface and speed.")
            + (goal != Goal.Drag && i.FrontWeightPct > 47 ? " Nose-heavy, so rear downforce is raised to keep balance." : "")
            + (goal != Goal.Drag && i.FrontWeightPct < 47 ? " Rear-weight bias, so rear downforce eases off the balanced point." : "")
            + (goal != Goal.Drag && i.EngineLocation == EngineLocation.Rear ? " Rear-engine: rear downforce held at least even with the front." : "")
            + ((HasRange(fR) || HasRange(rR)) ? lbfNote : "");
        string formula =
            $"front = level({S(level)}) × frontRange\nrear  = front + (frontWeight − 47) × 1.867   (balanced at 47%)" + ((HasRange(fR) || HasRange(rR)) ? lbfFormula : "");

        return new Aero(true, fp2, frontLbf, rp2, rearLbf, new Why(text, formula));
    }

    // ======================================================================
    // BRAKING — balance (% front) & pressure (%) — legacy 543-573
    // ======================================================================
    private static Braking Braking(TuneInput i, Derived d, Goal goal)
    {
        double bias = i.Drivetrain switch { Drivetrain.RWD => 52, Drivetrain.AWD => 54, Drivetrain.FWD => 58, _ => 53 };
        bias += Clamp((i.FrontWeightPct - 50) * 0.5, -6, 6);
        double engBias = i.EngineLocation == EngineLocation.Rear ? -1.0 : i.EngineLocation == EngineLocation.Mid ? -0.5 : 0;
        bias += engBias;
        // EV regen by drive axle
        bias += i.Powertrain == Powertrain.EV ? (i.Drivetrain == Drivetrain.FWD ? -1 : i.Drivetrain == Drivetrain.AWD ? 0.5 : 1) : i.Powertrain == Powertrain.Hybrid ? 0.5 : 0;
        bool lowGrip = i.TireCompound == TireCompound.Rally || i.TireCompound == TireCompound.Offroad;
        // goal rearward shift — { Drag:3, OffRoad:-4, Rally:-3, Touge:-1, Circuit:0 }[goal] || 0
        double goalShift = goal switch
        {
            Goal.Drag => 3,
            Goal.OffRoad => -4,
            Goal.Rally => -3,
            Goal.Touge => -1,
            Goal.Circuit => 0,
            _ => 0,
        };
        double rearShift = (lowGrip ? -3 : 0) + goalShift;
        if (goal != Goal.Drift) { bias += Math.Max(rearShift, -4 + (rearShift > 0 ? rearShift : 0)); }
        if (goal == Goal.Drift) bias = 48;
        bias = RInt(Clamp(bias, 40, 65));

        double pres = 100;
        pres += Clamp((i.Weight - 3000) / 1000 * 5, -10, 12);
        // { Race:5, Sport:2, Street:0, Drag:5, Rally:-5, Offroad:-8, Stock:0 }[tireCompound] || 0
        pres += i.TireCompound switch
        {
            TireCompound.Race => 5,
            TireCompound.Sport => 2,
            TireCompound.Street => 0,
            TireCompound.Drag => 5,
            TireCompound.Rally => -5,
            TireCompound.Offroad => -8,
            TireCompound.Stock => 0,
            _ => 0,
        };
        pres -= d.EvFactor * 3;
        // { Circuit:5, Drag:-10, Drift:-5, OffRoad:-10, Rally:-5, Touge:3 }[goal] || 0
        pres += goal switch
        {
            Goal.Circuit => 5,
            Goal.Drag => -10,
            Goal.Drift => -5,
            Goal.OffRoad => -10,
            Goal.Rally => -5,
            Goal.Touge => 3,
            _ => 0,
        };
        pres = R5(Clamp(pres, 80, 130));

        string text =
            $"{S(bias)}% front balance suits {DrivetrainToken(i.Drivetrain)} with {S(R1(i.FrontWeightPct))}% front weight — enough front brake to use forward weight transfer without locking the rear. " +
            (goal == Goal.Drift ? "Drift overrides to 48% (rear-biased) so braking helps rotate the car. " : "") +
            $"Pressure {S(pres)}% is tuned so tires lock only in the last sliver of trigger pull on {TireCompoundToken(i.TireCompound)} tires" + (goal == Goal.OffRoad || goal == Goal.Rally ? ", eased for the loose surface." : ".");
        string formula =
            "balance = dtBase + (frontPct−50)×0.5 + goal  (clamp 40–65)\npressure = 100 + massAdj + compound + goal  (clamp 80–130, step 5)";

        return new Braking(bias, pres, new Why(text, formula));
    }

    // ======================================================================
    // DIFFERENTIAL — accel/decel lock %, per axle (+ AWD center) — legacy 578-641
    // ======================================================================
    private static Differential Differential(TuneInput i, Derived d, Goal goal)
    {
        double trim = Clamp(-((d.Pw - 0.13) / 0.05) * 6, -16, 10);
        double ptAccel = i.Powertrain == Powertrain.EV ? -6 : i.Powertrain == Powertrain.Hybrid ? -3 : 0;
        bool lowGrip = i.TireCompound == TireCompound.Rally || i.TireCompound == TireCompound.Offroad;

        // DG[goal] — each driveline has its own per-goal array (nullable elements modeled as double?)
        (double?[] rwd, double?[] fwd, double?[] awd) dgv = goal switch
        {
            Goal.Circuit => (new double?[] { 0, 0 }, new double?[] { 0, 0 }, new double?[] { 0, 0, 0, 0, null, 0 }),
            Goal.Drag => (new double?[] { null, null, 90, 0 }, new double?[] { null, null, 90 }, new double?[] { 10, 0, 20, 0, 85 }),
            Goal.Drift => (new double?[] { null, null, 100, 30 }, new double?[] { null, null, 90 }, new double?[] { 20, 10, 30, 20, 88 }),
            Goal.OffRoad => (new double?[] { 24, null, null, 20 }, new double?[] { 20, 7 }, new double?[] { 10, 8, -10, -8, 60 }),
            Goal.Rally => (new double?[] { 12, 5 }, new double?[] { 15, 7 }, new double?[] { 5, 5, -15, -8, 70 }),
            Goal.Touge => (new double?[] { 18, -2 }, new double?[] { 10, 0 }, new double?[] { -10, 0, -5, -10, 80 }),
            _ => (new double?[] { 0, 0 }, new double?[] { 0, 0 }, new double?[] { 0, 0, 0, 0, null, 0 }),
        };

        if (i.Drivetrain == Drivetrain.FWD)
        {
            double?[] f = dgv.fwd; // [accelAdj, decelAdj, accelOverride?]
            // accel = f[2] != null ? f[2] : 60 + trim + f[0] + ptAccel
            double accel = Elem(f, 2) is double f2 ? f2 : 60 + trim + (Elem(f, 0) ?? 0) + ptAccel;
            // NOTE: JS uses f[0] directly (not f[0]||0) in the else branch. f[0] is always defined for FWD here.
            // decel = 8 + (f[1] || 0)
            double decel = 8 + (Falsy0(Elem(f, 1)));
            if (lowGrip) accel -= 6;
            accel = REven(Clamp(accel, 0, 95)); decel = RInt(Clamp(decel, 5, 100));
            return new Differential(Drivetrain.FWD, accel, decel, null, null, null, DiffWhy(Drivetrain.FWD, goal, accel, decel, i, null));
        }
        if (i.Drivetrain == Drivetrain.AWD)
        {
            double?[] a = dgv.awd; // [fAccelAdj,fDecelAdj,rAccelAdj,rDecelAdj,centerOverride]
            double fA = 30 + trim * 0.5 + (Elem(a, 0) ?? 0) + ptAccel * 0.5;
            double fD = 5 + (Elem(a, 1) ?? 0);
            double rA = 80 + trim + (Elem(a, 2) ?? 0) + ptAccel;
            double rD = 30 + (Elem(a, 3) ?? 0);
            // center = a[4] != null ? a[4] : 78
            double center = Elem(a, 4) is double a4 ? a4 : 78;
            center += Clamp((i.FrontWeightPct - 50) * 0.4, -6, 8);
            if (i.Power >= 600) center += 3;
            if (i.EngineLocation != EngineLocation.Front) rA -= 4;
            if (lowGrip) { rA -= 6; fA -= 6; }
            if (d.OversteerProne && goal != Goal.Drift && goal != Goal.Drag) { center -= 14; rA -= 22; rD -= 12; }
            double accelOut = REven(Clamp(rA, 0, 100));
            double decelOut = RInt(Clamp(rD, 0, 100));
            double centerOut = RInt(Clamp(center, 50, 90));
            return new Differential(
                Drivetrain.AWD,
                accelOut, decelOut,
                REven(Clamp(fA, 0, 95)), RInt(Clamp(fD, 0, 100)),
                centerOut,
                DiffWhy(Drivetrain.AWD, goal, accelOut, decelOut, i, centerOut));
        }
        // RWD
        double?[] r = dgv.rwd; // [accelAdj, decelAdj, accelOverride?, decelOverride?]
        double rAccel = Elem(r, 2) is double r2 ? r2 : 55 + trim + (Falsy0(Elem(r, 0))) + ptAccel;
        double rDecel = Elem(r, 3) is double r3 ? r3 : 20 + (Falsy0(Elem(r, 1)));
        if (i.EngineLocation != EngineLocation.Front) rAccel -= 4;
        if (lowGrip) rAccel -= 6;
        rAccel = REven(Clamp(rAccel, 0, 100)); rDecel = RInt(Clamp(rDecel, 0, 100));
        return new Differential(Drivetrain.RWD, rAccel, rDecel, null, null, null, DiffWhy(Drivetrain.RWD, goal, rAccel, rDecel, i, null));
    }

    // Array element access mirroring JS arr[idx] (undefined → null when out of range).
    private static double? Elem(double?[] arr, int idx) => idx >= 0 && idx < arr.Length ? arr[idx] : null;

    // JS `(x || 0)` for a possibly-null/0 number: null → 0, and 0 → 0 (falsy). NaN→0 too but
    // these tables hold no NaN. Non-zero passes through.
    private static double Falsy0(double? x) => (x is null || x.Value == 0) ? 0 : x.Value;

    private static Why DiffWhy(Drivetrain dl, Goal goal, double accel, double decel, TuneInput i, double? center)
    {
        string t;
        if (dl == Drivetrain.AWD)
            t = center >= 70
                ? $"AWD runs three diffs. Center sends {S(center!.Value)}% torque to the rear (floored at 50%) so it rotates like a sharpened RWD; rear locks harder than front. {GoalName(goal)}: rear {S(accel)}/{S(decel)}%."
                : $"AWD runs three diffs. Center sends {S(center!.Value)}% torque to the rear — held closer to balanced to settle a tail-happy chassis; rear locks {S(accel)}/{S(decel)}%. {GoalName(goal)}.";
        else if (dl == Drivetrain.FWD)
            t = $"Front accel {S(accel)}% (capped 95) puts power down without killing turn-in; decel {S(decel)}% keeps it stable on lift. {GoalName(goal)} tune.";
        else
            t = $"Accel lock {S(accel)}% controls corner-exit traction; decel {S(decel)}% sets off-throttle stability. {GoalName(goal)}" + (goal == Goal.Drift ? " runs a near-welded rear to hold angle." : goal == Goal.Drag ? " locks both rears for an even launch." : ".");
        return new Why(t, $"{DrivetrainToken(dl)} base ± power-trim ± powertrain ± {GoalName(goal)} adj ; accel even %, clamp 0–100");
    }

    // ======================================================================
    // HANDLING BIAS + OVERALL STIFFNESS post-processors — legacy 656-831
    // ======================================================================

    // signed non-linear scale: sign(b)·(min(|b|,5)/5)^exp → −1 … +1 (lines 656-659)
    private static double BiasScale(double b, double exp)
    {
        double m = Math.Pow(Math.Min(Math.Abs(b), 5) / 5, exp);
        return b < 0 ? -m : m;
    }

    private static string DirWord(double b) => b > 0 ? "oversteer" : "understeer";

    private static Why BiasNote(Why? why, string msg) =>
        why is null ? why! : why with { Text = why.Text + "  Handling bias: " + msg + "." };

    private static Why StiffNote(Why? why, string msg) =>
        why is null ? why! : why with { Text = why.Text + "  Overall stiffness: " + msg + "." };

    private static Tune ApplyHandlingBias(Tune t, TuneInput input, double bias)
    {
        Derived d = t.Derived;
        Arb arb = t.Arb;
        Springs springs = t.Springs;
        Differential diff = t.Differential;
        Braking braking = t.Braking;
        Aero aero = t.Aero;

        /* ---- ARB front/rear ratio (±8% each end, exp 1.2) ---- */
        if (d.CanTuneSusp)
        {
            double s = BiasScale(bias, 1.2); // + → softer front / stiffer rear
            double front = Clamp(R2(arb.Front * (1 - 0.08 * s)), 1, 65);
            double rear = Clamp(R2(arb.Rear * (1 + 0.08 * s)), 1, 65);
            Why arbWhy = BiasNote(arb.Why, bias > 0
                ? "front bar softened / rear bar stiffened → less front roll-grip used, freer rotation → promotes oversteer"
                : "front bar stiffened / rear bar softened → more rear plant, calmer nose → promotes understeer");
            arb = arb with { Front = front, Rear = rear, Why = arbWhy };
        }

        /* ---- Front/rear spring ratio (front ±12%, rear ±4%, exp 1.1) ---- */
        if (d.CanTuneSusp)
        {
            double s = BiasScale(bias, 1.1); // + → softer front / stiffer rear
            double minF = input.SpringRateMinF ?? input.SpringRateMin ?? double.NaN;
            double maxF = input.SpringRateMaxF ?? input.SpringRateMax ?? double.NaN;
            double minR = input.SpringRateMinR ?? input.SpringRateMin ?? double.NaN;
            double maxR = input.SpringRateMaxR ?? input.SpringRateMax ?? double.NaN;
            double front = RInt(Clamp(springs.Front * (1 - 0.12 * s), minF, maxF));
            double rear = RInt(Clamp(springs.Rear * (1 + 0.04 * s), minR, maxR));
            Why sWhy = BiasNote(springs.Why, bias > 0
                ? "front springs softened ~12% / rear stiffened ~4% → rear-biased frequency, front loads up → promotes oversteer"
                : "front springs stiffened ~12% / rear softened ~4% → front-biased frequency, rear plants → promotes understeer");
            springs = springs with { Front = front, Rear = rear, Why = sWhy };
        }

        /* ---- Differential ---- */
        {
            Drivetrain dl = diff.Driveline;
            bool isDrift = t.Goal == Goal.Drift;
            double sA = BiasScale(bias, 1.4); // accel: ±12 pts
            double sD = BiasScale(bias, 1.2); // decel: ±8 pts
            bool lockMoved = false, centerMoved = false;
            double accel = diff.Accel, decel = diff.Decel;
            double? centerRear = diff.CenterRear;
            if (!isDrift && (dl == Drivetrain.RWD || dl == Drivetrain.AWD))
            {
                accel = REven(Clamp(diff.Accel + 12 * sA, 0, 100));
                decel = RInt(Clamp(diff.Decel + 8 * sD, 0, 100));
                lockMoved = true;
            }
            else if (!isDrift && dl == Drivetrain.FWD)
            {
                accel = REven(Clamp(diff.Accel + 12 * sA, 0, 95));
                decel = RInt(Clamp(diff.Decel + 8 * sD, 5, 100));
                lockMoved = true;
            }
            if (dl == Drivetrain.AWD && diff.CenterRear != null)
            {
                double sC = BiasScale(bias, 1.1);
                centerRear = RInt(Clamp(diff.CenterRear.Value + 6 * sC, 50, 90));
                centerMoved = true;
            }
            Why diffWhy = diff.Why;
            if (lockMoved || centerMoved)
            {
                List<string> parts = [];
                if (lockMoved) parts.Add("accel/decel lock " + (bias > 0 ? "raised" : "lowered"));
                if (centerMoved) parts.Add("torque sent " + (bias > 0 ? "further rearward" : "forward"));
                diffWhy = BiasNote(diff.Why, string.Join(" and ", parts) +
                    (bias > 0 ? " → tighter/more rearward drive → tuned toward oversteer"
                              : " → looser/more frontward drive → tuned toward understeer"));
            }
            diff = diff with { Accel = accel, Decel = decel, CenterRear = centerRear, Why = diffWhy };
        }

        /* ---- Brake balance (±4 pts, linear; NOT applied to Drift) ---- */
        if (t.Goal != Goal.Drift)
        {
            double s = bias / 5; // linear per spec
            double balance = RInt(Clamp(braking.Balance + 4 * s, 40, 65));
            Why bWhy = BiasNote(braking.Why, bias > 0
                ? "brake balance shifted forward → front loads first under braking, calmer rear → entry tuned toward oversteer control with front grip"
                : "brake balance shifted rearward → rear eases off, front frees → entry tuned toward understeer reduction");
            braking = braking with { Balance = balance, Why = bWhy };
        }

        /* ---- Aero front/rear BALANCE (±0.08 front-share at ±5, exp 1.05) ---- */
        if (aero.Applicable && t.Goal != Goal.Drag)
        {
            double s = BiasScale(bias, 1.05); // + → more front DF / less rear DF (toward oversteer)
            AeroRange fR = input.AeroFront, rR = input.AeroRear;
            double? BiasLbf(double pct, AeroRange rng) => rng.HasRange
                ? JsMath.Round(rng.Min!.Value + (rng.Max!.Value - rng.Min!.Value) * Clamp(pct / 100, 0, 1))
                : (double?)null;

            if (aero.Front != null && aero.Rear != null)
            {
                // Full kit: shift the aero BALANCE (front-share), preserving total downforce. Work in
                // lbf when ranges are known (kit-independent), else in %-share. This mirrors the
                // baseline's magnitude model so the dial feels the same on every car's kit.
                bool useLbf = aero.FrontLbf is double && aero.RearLbf is double && fR.HasRange && rR.HasRange;
                double fVal = useLbf ? aero.FrontLbf!.Value : aero.Front.Value;
                double rVal = useLbf ? aero.RearLbf!.Value : aero.Rear.Value;
                double total = fVal + rVal;
                if (total > 0)
                {
                    double newShare = Clamp(fVal / total + 0.08 * s, 0, 1);
                    double nf = total * newShare, nr = total * (1 - newShare);
                    if (useLbf)
                    {
                        nf = Clamp(nf, fR.Min!.Value, fR.Max!.Value);
                        nr = Clamp(nr, rR.Min!.Value, rR.Max!.Value);
                        aero = aero with
                        {
                            Front = R5((nf - fR.Min.Value) / (fR.Max.Value - fR.Min.Value) * 100),
                            FrontLbf = JsMath.Round(nf),
                            Rear = R5((nr - rR.Min.Value) / (rR.Max.Value - rR.Min.Value) * 100),
                            RearLbf = JsMath.Round(nr),
                        };
                    }
                    else
                    {
                        aero = aero with { Front = R5(Clamp(nf, 0, 100)), Rear = R5(Clamp(nr, 0, 100)) };
                    }
                    aero = aero with
                    {
                        Why = BiasNote(aero.Why, bias > 0
                            ? "aero balance shifted forward → more front high-speed grip, looser rear → toward oversteer"
                            : "aero balance shifted rearward → rear planted at speed, lighter nose → toward understeer"),
                    };
                }
            }
            else if (aero.Front != null)
            {
                // Single front splitter — can't rebalance; nudge the present end ±8% of its range.
                double nf = R5(Clamp(aero.Front.Value + 8 * s, 0, 100));
                aero = aero with { Front = nf, FrontLbf = BiasLbf(nf, fR),
                    Why = BiasNote(aero.Why, bias > 0 ? "front downforce raised → toward oversteer" : "front downforce lowered → toward understeer") };
            }
            else if (aero.Rear != null)
            {
                // Single rear wing — nudge the present end ∓8% of its range.
                double nr = R5(Clamp(aero.Rear.Value - 8 * s, 0, 100));
                aero = aero with { Rear = nr, RearLbf = BiasLbf(nr, rR),
                    Why = BiasNote(aero.Why, bias > 0 ? "rear downforce lowered → toward oversteer" : "rear downforce raised → toward understeer") };
            }
        }

        return t with { Arb = arb, Springs = springs, Differential = diff, Braking = braking, Aero = aero };
    }

    private static Tune ApplyOverallStiffness(Tune t, TuneInput input, double stiff)
    {
        Derived d = t.Derived;
        // Stock suspension exempts every lever.
        if (!d.CanTuneSusp) return t;
        bool hard = stiff > 0;

        Springs springs = t.Springs;
        Arb arb = t.Arb;
        Damping damping = t.Damping;

        /* ---- Spring rate front & rear (±25% each, exp 1.1) + ride height ---- */
        {
            double s = BiasScale(stiff, 1.1); // + → stiffer / lower
            double minF = input.SpringRateMinF ?? input.SpringRateMin ?? double.NaN;
            double maxF = input.SpringRateMaxF ?? input.SpringRateMax ?? double.NaN;
            double minR = input.SpringRateMinR ?? input.SpringRateMin ?? double.NaN;
            double maxR = input.SpringRateMaxR ?? input.SpringRateMax ?? double.NaN;
            double front = RInt(Clamp(springs.Front * (1 + 0.25 * s), minF, maxF));
            double rear = RInt(Clamp(springs.Rear * (1 + 0.25 * s), minR, maxR));
            double rhMinF = input.RideHeightMinF ?? input.RideHeightMin ?? double.NaN;
            double rhMaxF = input.RideHeightMaxF ?? input.RideHeightMax ?? double.NaN;
            double rhMinR = input.RideHeightMinR ?? input.RideHeightMin ?? double.NaN;
            double rhMaxR = input.RideHeightMaxR ?? input.RideHeightMax ?? double.NaN;
            double rideF = R1(Clamp(springs.RideF - (rhMaxF - rhMinF) * 0.15 * s, rhMinF, rhMaxF));
            double rideR = R1(Clamp(springs.RideR - (rhMaxR - rhMinR) * 0.15 * s, rhMinR, rhMaxR));
            Why sWhy = StiffNote(springs.Why, hard
                ? "spring rates raised ~25% and ride height lowered → a firmer, lower, flatter platform"
                : "spring rates lowered ~25% and ride height raised → a softer, taller, more compliant platform");
            springs = springs with { Front = front, Rear = rear, RideF = rideF, RideR = rideR, Why = sWhy };
        }

        /* ---- Anti-roll bars front & rear (±30% each, exp 1.15) ---- */
        {
            double s = BiasScale(stiff, 1.15);
            double front = Clamp(R2(arb.Front * (1 + 0.30 * s)), 1, 65);
            double rear = Clamp(R2(arb.Rear * (1 + 0.30 * s)), 1, 65);
            Why aWhy = StiffNote(arb.Why, hard
                ? "both anti-roll bars stiffened ~30% → less body roll, sharper turn-in"
                : "both anti-roll bars softened ~30% → more roll, more mechanical grip over bumps");
            arb = arb with { Front = front, Rear = rear, Why = aWhy };
        }

        /* ---- Damping: bump + rebound, all four corners (±25%, exp 1.1) ---- */
        {
            double k = 1 + 0.25 * BiasScale(stiff, 1.1);
            double bumpF = R1(Clamp(damping.BumpF * k, 1, 20));
            double bumpR = R1(Clamp(damping.BumpR * k, 1, 20));
            double reboundF = R1(Clamp(damping.ReboundF * k, 1, 20));
            double reboundR = R1(Clamp(damping.ReboundR * k, 1, 20));
            Why dWhy = StiffNote(damping.Why, hard
                ? "bump and rebound raised ~25% in proportion → tighter body control"
                : "bump and rebound lowered ~25% in proportion → softer, more absorbent damping");
            damping = damping with { BumpF = bumpF, BumpR = bumpR, ReboundF = reboundF, ReboundR = reboundR, Why = dWhy };
        }

        return t with { Springs = springs, Arb = arb, Damping = damping };
    }

    // ======================================================================
    // VALIDATE — legacy 840-898
    // ======================================================================
    public ValidationResult Validate(RawInput input)
    {
        List<string> errors = [];
        RawInput i = input;

        // isNum(v): typeof number && isFinite(v). validate calls isNum(Number(i[key])).
        static bool IsNum(double v) => JsNumber.IsFiniteNumber(v);

        // required numerics must be present & finite
        // required = { power: "Power (hp)", weight: "Weight (lb)", frontWeightPct: "Front weight %" }
        // for each key: if i[key] === undefined || null || "" || !isNum(Number(i[key])) → push "... is required and must be a number."
        CheckRequired(errors, i.Power, "Power (hp)");
        CheckRequired(errors, i.Weight, "Weight (lb)");
        CheckRequired(errors, i.FrontWeightPct, "Front weight %");

        // weight must be a positive mass — if isNum(Number(weight)) && Number(weight) <= 0
        double weightNum = JsNumber.Coerce(i.Weight);
        if (IsNum(weightNum) && weightNum <= 0)
            errors.Add("Weight must be greater than 0.");

        // front weight % strictly inside 0–100
        double fwNum = JsNumber.Coerce(i.FrontWeightPct);
        if (IsNum(fwNum))
        {
            double fw = fwNum;
            if (fw <= 0) errors.Add("Front weight % must be greater than 0.");
            else if (fw >= 100) errors.Add("Front weight % must be less than 100.");
        }

        // power can't be negative
        double powerNum = JsNumber.Coerce(i.Power);
        if (IsNum(powerNum) && powerNum < 0)
            errors.Add("Power cannot be negative.");

        // torque, when supplied, can't be negative — guard: torque !== undefined && !== null && !== "" && isNum(Number) && < 0
        if (!IsAbsentOrEmpty(i.Torque))
        {
            double torqueNum = JsNumber.Coerce(i.Torque);
            if (IsNum(torqueNum) && torqueNum < 0)
                errors.Add("Torque cannot be negative.");
        }

        // gears: at least 1 — guard: gears !== undefined && !== null && !== ""
        if (!IsAbsentOrEmpty(i.Gears))
        {
            double g = JsNumber.Coerce(i.Gears);
            if (!IsNum(g)) errors.Add("Number of gears must be a number.");
            else if (g < 1) errors.Add("Number of gears must be at least 1.");
        }

        // per-axle ranges with shared fallback; pick(a,b) = (a present non-empty) ? a : b
        CheckRange(errors, Pick(i.SpringRateMinF, i.SpringRateMin), Pick(i.SpringRateMaxF, i.SpringRateMax), "Front spring rate");
        CheckRange(errors, Pick(i.SpringRateMinR, i.SpringRateMin), Pick(i.SpringRateMaxR, i.SpringRateMax), "Rear spring rate");
        CheckRange(errors, Pick(i.RideHeightMinF, i.RideHeightMin), Pick(i.RideHeightMaxF, i.RideHeightMax), "Front ride height");
        CheckRange(errors, Pick(i.RideHeightMinR, i.RideHeightMin), Pick(i.RideHeightMaxR, i.RideHeightMax), "Rear ride height");

        return new ValidationResult(errors.Count == 0, errors);
    }

    // i[key] === undefined || null || "" || !isNum(Number(i[key]))
    private static void CheckRequired(List<string> errors, RawValue v, string label)
    {
        bool missing = v.IsAbsent || (v.Kind == RawKind.Str && v.AsString == "");
        if (missing || !JsNumber.IsFiniteNumber(JsNumber.Coerce(v)))
            errors.Add($"{label} is required and must be a number.");
    }

    // JS: value !== undefined && value !== null && value !== "" — true when present & non-empty.
    private static bool IsAbsentOrEmpty(RawValue v) =>
        v.IsAbsent || (v.Kind == RawKind.Str && v.AsString == "");

    // pick(a, b) = (a !== undefined && a !== null && a !== "") ? a : b
    private static RawValue Pick(RawValue a, RawValue b) =>
        (!a.IsAbsent && !(a.Kind == RawKind.Str && a.AsString == "")) ? a : b;

    // checkRange(loRaw, hiRaw, label): lo=Number(loRaw), hi=Number(hiRaw);
    //   if isNum(lo) && isNum(hi): if lo<=0 push min err; if hi<lo push max err.
    private static void CheckRange(List<string> errors, RawValue loRaw, RawValue hiRaw, string label)
    {
        double lo = JsNumber.Coerce(loRaw), hi = JsNumber.Coerce(hiRaw);
        if (JsNumber.IsFiniteNumber(lo) && JsNumber.IsFiniteNumber(hi))
        {
            if (lo <= 0) errors.Add($"{label} min must be greater than 0.");
            if (hi < lo) errors.Add($"{label} max must be greater than or equal to {label.ToLowerInvariant()} min.");
        }
    }

    // ======================================================================
    // TOP-LEVEL COMPUTE — legacy 903-934
    // ======================================================================
    public Tune Compute(TuneInput input, Goal goal)
    {
        Derived d = Derive(input);
        var (spr, _, _) = Springs(input, d, goal);
        Tune tune = new(
            Goal: goal,
            Derived: d,
            Summary:
            [
                new SummaryChip("Power-to-weight", $"{S(R2(d.Pw))} hp/lb"),
                new SummaryChip("Front corner", $"{S(RInt(d.FrontCorner))} lb"),
                new SummaryChip("Rear corner", $"{S(RInt(d.RearCorner))} lb"),
                new SummaryChip("Balance", $"{S(R1(d.Frac * 100))}/{S(R1(d.RearFrac * 100))}"),
                new SummaryChip("Class tier", ClassTierToken(d.ClassTier)),
            ],
            Tires: Tires(input, d, goal),
            Gearing: Gearing(input, d, goal),
            Alignment: Alignment(input, d, goal),
            Arb: Arb(input, d, goal),
            Springs: spr,
            Damping: Damping(input, d, goal, spr.Front, spr.Rear),
            Aero: Aero(input, d, goal),
            Braking: Braking(input, d, goal),
            Differential: Differential(input, d, goal));

        // Post-process dials. At 0 each is skipped (baseline byte-for-byte). Stiffness first.
        double stiff = input.OverallStiffness; // non-null double defaulting to 0; JS: Number(x) || 0
        if (stiff != 0) tune = ApplyOverallStiffness(tune, input, Clamp(stiff, -5, 5));
        double bias = input.HandlingBias;
        if (bias != 0) tune = ApplyHandlingBias(tune, input, Clamp(bias, -5, 5));
        return tune;
    }

    // ======================================================================
    // JS-token helpers (the strings the why text interpolates from input enums)
    // ======================================================================
    private static string DrivetrainToken(Drivetrain dt) => dt.ToString();
    private static string TireCompoundToken(TireCompound c) => c.ToString();
    private static string PiClassToken(PiClass c) => c.ToString();
    private static string ClassTierToken(ClassTier c) => c.ToString();
}
