# FH6 Tuning Calculator — C# / .NET 10 Blazor WASM Port: Authoritative Architecture

This is the single source of truth for the C# port. It reconciles the two architecture
proposals into one plan, resolved against the **actual** legacy source under `legacy/`.
Implementing agents should follow it without re-deciding anything. Where the two proposals
disagreed, the resolution and its justification (with a `legacy/` citation) are called out
in **Resolved decisions** (§14).

The hard contract is **byte-for-byte numeric parity** with `legacy/tuning.js`, verified by a
differential harness that runs the real JS engine in Node and diffs canonicalized JSON
against the C# engine (§9).

---

## 0. Repository layout & build topology

```
C:\git\fh6-tuning-calculator\
├─ Fh6Tuning.sln                     ← solution at repo ROOT
├─ legacy\                           ← UNTOUCHED parity oracle (tuning.js, app.js, setups.js, index.html, styles.css, sweep.js, test\)
├─ research\                         ← UNTOUCHED formula derivations (spec-*.md)
├─ Fh6Tuning.Core\                   ← class library: domain model + engine + pure storage logic
├─ Fh6Tuning.Web\                    ← Blazor WASM + MudBlazor (the ONLY project that touches DOM/JS/localStorage)
├─ Fh6Tuning.Tests\                  ← xUnit; references Core; shells out to Node for parity
├─ parity\                           ← generated parity artifacts (cases.json, jsmath-oracle.json); committed
└─ .github\workflows\pages.yml       ← GitHub Pages publish pipeline
```

### Project files

All three target **`net10.0`**. SDK confirmed `10.0.301`; node confirmed `v22.20.0`.

**`Fh6Tuning.Core\Fh6Tuning.Core.csproj`** — `Microsoft.NET.Sdk`, no package dependencies.
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```
`InvariantGlobalization` guarantees `double`→string and parse use invariant culture, so
the `.` decimal separator is fixed regardless of the build/host locale — load-bearing for
both the parity harness and the `nf` formatter.

**`Fh6Tuning.Web\Fh6Tuning.Web.csproj`** — `Microsoft.NET.Sdk.BlazorWebAssembly`.
```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
    <ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MudBlazor" Version="8.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Fh6Tuning.Core\Fh6Tuning.Core.csproj" />
  </ItemGroup>
</Project>
```

**`Fh6Tuning.Tests\Fh6Tuning.Tests.csproj`** — `Microsoft.NET.Sdk`, xUnit. References Core.
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Fh6Tuning.Core\Fh6Tuning.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <!-- parity case data + node scripts copied next to the test assembly -->
    <None Include="..\parity\**\*" CopyToOutputDirectory="PreserveNewest" LinkBase="parity\" />
    <None Include="Parity\run-legacy.mjs" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

### Scaffolding sequence (for the first implementer)
```
dotnet new sln -n Fh6Tuning
dotnet new classlib   -n Fh6Tuning.Core -f net10.0 -o Fh6Tuning.Core
dotnet new blazorwasm -n Fh6Tuning.Web  -f net10.0 -o Fh6Tuning.Web
dotnet new xunit      -n Fh6Tuning.Tests -f net10.0 -o Fh6Tuning.Tests
dotnet sln add Fh6Tuning.Core Fh6Tuning.Web Fh6Tuning.Tests
dotnet add Fh6Tuning.Web   reference Fh6Tuning.Core
dotnet add Fh6Tuning.Tests reference Fh6Tuning.Core
dotnet add Fh6Tuning.Web   package MudBlazor
# delete the default classlib Class1.cs and the blazorwasm sample (Counter/Weather) pages
```

---

## 1. `JsMath` — the rounding/parity core (non-negotiable)

`Fh6Tuning.Core\JsMath.cs`. Mirrors `legacy/tuning.js` lines 40–46 exactly. JS `Math.round`
rounds half **toward +∞** (`round(0.5)=1`, `round(2.5)=3`, `round(-0.5)=0`, `round(-2.5)=-2`).
C# `Math.Round` is banker's by default — **NEVER use `Math.Round` in Core.** All math is
`double` (IEEE-754, identical to JS `Number`); **never `decimal`, never `float`.**

```csharp
namespace Fh6Tuning.Core;

public static class JsMath
{
    /// <summary>JS Math.round: half rounds toward +Infinity.</summary>
    public static double Round(double x) => Math.Floor(x + 0.5);

    /// <summary>legacy clamp(x,lo,hi) = Math.min(hi, Math.max(lo, x)).</summary>
    public static double Clamp(double x, double lo, double hi) => Math.Min(hi, Math.Max(lo, x));

    public static double R1(double x)    => Round(x * 10) / 10;    // r1: 1 decimal
    public static double R2(double x)    => Round(x * 100) / 100;  // r2: 2 decimals
    public static double RHalf(double x) => Round(x * 2) / 2;      // rHalf: nearest 0.5
    public static double R5(double x)    => Round(x / 5) * 5;      // r5: nearest 5
    public static double RInt(double x)  => Round(x);             // rInt: integer
    public static double REven(double x) => Round(x / 2) * 2;      // rEven: nearest even

    /// <summary>
    /// JSON.stringify(-0) === "0" in JS, but System.Text.Json writes "-0".
    /// Apply to every signed rounded output at the point it is assigned to a record.
    /// No-op for any non-negative-zero value.
    /// </summary>
    public static double NormZero(double x) => x == 0.0 ? 0.0 : x;
}
```

### `JsNumber` — JS `Number()` / `parseFloat` coercion (Core)

`Fh6Tuning.Core\JsNumber.cs`. `validate()` and the Web parse layer both depend on JS number
coercion semantics. Centralize them so they match exactly.

```csharp
namespace Fh6Tuning.Core;

public static class JsNumber
{
    /// <summary>
    /// Mirrors JS Number(v) on the values validate() actually sees (legacy/tuning.js:852,
    /// 858, 862-880). The raw value comes from RawValue (string | double | bool | null):
    ///   null/undefined  → NaN   (validate treats it via the isNum guard)
    ///   "" (empty/blank)→ 0     (JS Number("")===0 and Number("  ")===0)
    ///   numeric string  → that number ("1e3"→1000, " 12 "→12)
    ///   non-numeric str → NaN   ("heavy"→NaN)
    ///   a real double   → itself (NaN/±Infinity pass through unchanged)
    /// </summary>
    public static double Coerce(RawValue v) => /* see RawValue, §7 */;

    /// <summary>JS parseFloat: leading-number parse, NaN on no leading number.
    /// Used by app.js num()/parseFloat call sites in the Web layer.</summary>
    public static double ParseFloat(string? s);

    /// <summary>JS isFinite(Number(v)) — typeof number && isFinite in the engine.</summary>
    public static bool IsFiniteNumber(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
}
```

> Note on JS `Number("")===0`: empty string coerces to **0**, not NaN. This matters for the
> `Number(input.handlingBias) || 0` / `Number(input.overallStiffness) || 0` dial reads
> (legacy/tuning.js:929,931): a blank string dial value becomes `0` (dials skipped). On the
> typed `TuneInput`, `HandlingBias`/`OverallStiffness` are non-null `double` defaulting to
> `0`, so `Compute` simply checks `!= 0`.

---

## 2. Enums

`Fh6Tuning.Core\Enums.cs`. Every categorical input is a C# enum whose **member names equal
the JS string tokens** used as dictionary keys throughout `legacy/tuning.js`. This lets a
single `JsonStringEnumConverter<T>` round-trip them and lets the engine `switch` on them with
no string layer. Declaration order of `PiClass` **is** the `PI_INDEX` ladder, so `(int)c`
equals the legacy index.

```csharp
using System.Text.Json.Serialization;

namespace Fh6Tuning.Core;

[JsonConverter(typeof(JsonStringEnumConverter<Drivetrain>))]
public enum Drivetrain { FWD, RWD, AWD }                       // legacy: "FWD"|"RWD"|"AWD"

[JsonConverter(typeof(JsonStringEnumConverter<EngineLocation>))]
public enum EngineLocation { Front, Mid, Rear }                // legacy: "Front"|"Mid"|"Rear"

[JsonConverter(typeof(JsonStringEnumConverter<Powertrain>))]
public enum Powertrain { ICE, EV, Hybrid }                     // legacy: "ICE"|"EV"|"Hybrid"

[JsonConverter(typeof(JsonStringEnumConverter<PiClass>))]
public enum PiClass { D, C, B, A, S1, S2, R, X }               // ladder index 0..7 == PI_INDEX

// legacy tire-compound keys (note lowercase 'r' in "Offroad" — DISTINCT from Goal.OffRoad)
[JsonConverter(typeof(JsonStringEnumConverter<TireCompound>))]
public enum TireCompound { Stock, Street, Sport, Race, Rally, Drag, Offroad }

[JsonConverter(typeof(JsonStringEnumConverter<SuspensionType>))]
public enum SuspensionType { Stock, Street, Sport, Race, Drift, Offroad }

// UI aero-kit selector; exploded into hasFrontAero/hasRearAero/aeroInstalled at read time.
[JsonConverter(typeof(JsonStringEnumConverter<AeroKit>))]
public enum AeroKit { None, Front, Rear, Full }                // legacy <select> values

// GOALS array order is canonical (legacy/tuning.js:28). NOTE capital-R "OffRoad".
[JsonConverter(typeof(JsonStringEnumConverter<Goal>))]
public enum Goal { Circuit, Drag, Drift, OffRoad, Rally, Touge }

// app.js concept only; the engine is imperial-only.
[JsonConverter(typeof(JsonStringEnumConverter<UnitSystem>))]
public enum UnitSystem { Imperial, Metric }

// derived.classTier: keys FREQ_BASE/MIN_BUMP; surfaces in the "Class tier" summary chip.
[JsonConverter(typeof(JsonStringEnumConverter<ClassTier>))]
public enum ClassTier { Sports, HighPerf, Race }
```

### PI ladder helper

```csharp
namespace Fh6Tuning.Core;

public static class PiClassInfo
{
    /// <summary>0..7 ladder index == legacy PI_INDEX. D=0,C=1,B=2,A=3,S1=4,S2=5,R=6,X=7.</summary>
    public static int Index(this PiClass c) => (int)c;

    public const int A  = (int)PiClass.A;   // 3
    public const int S1 = (int)PiClass.S1;  // 4
}
```

The legacy `PI_INDEX[i.piClass] != null ? ... : 3` fallback (line 59) cannot occur with a
non-nullable enum; document it but do not branch on it.

### Enum-token golden table (parity-critical)

`EnumNamingTests` asserts each member serializes to exactly this string. Silent casing drift
here is the single most likely source of parity failure:

| Enum value | JS token | | Enum value | JS token |
|---|---|---|---|---|
| `Goal.OffRoad` | `"OffRoad"` | | `TireCompound.Offroad` | `"Offroad"` |
| `SuspensionType.Offroad` | `"Offroad"` | | `PiClass.S1/S2/R/X` | `"S1"/"S2"/"R"/"X"` |
| `Drivetrain.FWD/RWD/AWD` | identical | | `EngineLocation.Front/Mid/Rear` | identical |
| `Powertrain.ICE/EV/Hybrid` | identical | | `ClassTier.HighPerf` | `"HighPerf"` |

---

## 3. `AeroRange` — the nullable `[min, max]` lbf pair

`Fh6Tuning.Core\AeroRange.cs`. In JS `aeroFront`/`aeroRear` are `[min, max]` arrays where
either or both elements may be `null` (`[null, null]` default); `hasRange` requires both
present (legacy/tuning.js:458–460). Modeled as a `record struct` of two `double?`.

```csharp
using System.Text.Json.Serialization;

namespace Fh6Tuning.Core;

[JsonConverter(typeof(Serialization.AeroRangeJsonConverter))]
public readonly record struct AeroRange(double? Min, double? Max)
{
    public static readonly AeroRange None = new(null, null);

    /// <summary>legacy hasRange: rng[0] != null && rng[1] != null.</summary>
    public bool HasRange => Min is not null && Max is not null;

    /// <summary>legacy toLbf(frac, rng): Math.round(min + (max-min)*clamp(frac,0,1)), or null.</summary>
    public double? ToLbf(double frac) =>
        HasRange ? JsMath.Round(Min!.Value + (Max!.Value - Min!.Value) * JsMath.Clamp(frac, 0, 1)) : null;
}
```

`AeroRangeJsonConverter` serializes as a 2-element JSON array `[min, max]` with `null`
preserved (to match `aeroFront: [122, 203]` / `[null, null]` in setups + parity inputs).

---

## 4. `TuneInput` — the engine input record (imperial only)

`Fh6Tuning.Core\TuneInput.cs`. Immutable record holding the **complete** field set the engine
reads, taken from `readInputs()` (legacy/app.js:84–123) + `derive()` + the per-axle fallbacks
in `springs()`/`validate()`. **All units imperial** (lb, in, lb/in, hp, lb-ft, mph, rpm); the
Web layer converts before constructing this.

Type resolutions (see §14):
- Every numeric field is `double` (never `decimal`/`float`).
- `Gears` is `double` and the engine applies `Math.round(i.gears)` then clamps `[2,10]`
  (legacy/tuning.js:192). Kept `double` so the value the Web layer parses (`intField` →
  `Math.round`) flows through with no second rounding surprise; the engine still re-rounds.
- Required engine numerics (`Power`, `Torque`, `Weight`, `FrontWeightPct`, `Gears`) are
  non-nullable `double`. The string/blank/NaN states `validate()` inspects live in `RawInput`
  (§7), not here — by the time a `TuneInput` exists, parsing has happened.
- Optional gearing physics (`RedlineRpm`, `TireDiameter`, `TargetTopSpeed`) are `double?`.
  The legacy guards are `RL > 0 && TD > 0` and `TT > 0` (lines 166, 203, 206) — so `null`
  **and** `<= 0` behave identically. The computed guards below collapse both.
- Per-axle spring/ride ranges are the source of truth; the legacy shared keys
  (`springRateMin/Max`, `rideHeightMin/Max`) are optional fallbacks. `readInputs` only ever
  emits per-axle keys, but `validate()`'s `failure.test.js` cases use the **shared** keys
  (e.g. `springRateMin: 900, springRateMax: 150`), so both must exist on `RawInput`; the
  engine resolves per-axle-then-shared.
- The two dials default to exactly `0` (the neutrality contract hinges on this).

```csharp
using System.Text.Json.Serialization;

namespace Fh6Tuning.Core;

public sealed record TuneInput
{
    // ---- categorical ----
    public required Drivetrain Drivetrain { get; init; }
    public required EngineLocation EngineLocation { get; init; }
    public required Powertrain Powertrain { get; init; }
    public required PiClass PiClass { get; init; }
    public required TireCompound TireCompound { get; init; }
    public required SuspensionType SuspensionType { get; init; }

    // ---- core numerics (required) ----
    public required double Power { get; init; }          // hp
    public required double Torque { get; init; }         // lb-ft
    public required double Weight { get; init; }         // lb
    public required double FrontWeightPct { get; init; } // 0..100
    public required double Gears { get; init; }          // engine does Math.round then clamp[2,10]

    // ---- aero kit booleans (engine reads these, not the kit) ----
    public required bool HasFrontAero { get; init; }
    public required bool HasRearAero { get; init; }
    public required bool AeroInstalled { get; init; }

    // ---- per-axle spring rate part range (lb/in) ----
    public double? SpringRateMinF { get; init; }
    public double? SpringRateMaxF { get; init; }
    public double? SpringRateMinR { get; init; }
    public double? SpringRateMaxR { get; init; }

    // ---- per-axle ride height part range (in) ----
    public double? RideHeightMinF { get; init; }
    public double? RideHeightMaxF { get; init; }
    public double? RideHeightMinR { get; init; }
    public double? RideHeightMaxR { get; init; }

    // ---- legacy shared range fallbacks ----
    public double? SpringRateMin { get; init; }
    public double? SpringRateMax { get; init; }
    public double? RideHeightMin { get; init; }
    public double? RideHeightMax { get; init; }

    // ---- optional downforce ranges (imperial lbf) ----
    public AeroRange AeroFront { get; init; } = AeroRange.None;
    public AeroRange AeroRear { get; init; } = AeroRange.None;

    // ---- optional gearing physics (null OR <=0 → HP heuristic) ----
    public double? RedlineRpm { get; init; }
    public double? TireDiameter { get; init; }    // rolling Ø, in (from OverallTireDiameter)
    public double? TargetTopSpeed { get; init; }  // mph

    // ---- post-process dials (0 = pure baseline) ----
    public double HandlingBias { get; init; } = 0;       // -5 understeer … +5 oversteer
    public double OverallStiffness { get; init; } = 0;   // -5 soft … +5 hard

    // ============ resolved accessors (engine reads these; mirror legacy != null ? : picks) ===
    [JsonIgnore] public double SpringMinF => SpringRateMinF ?? SpringRateMin ?? double.NaN;
    [JsonIgnore] public double SpringMaxF => SpringRateMaxF ?? SpringRateMax ?? double.NaN;
    [JsonIgnore] public double SpringMinR => SpringRateMinR ?? SpringRateMin ?? double.NaN;
    [JsonIgnore] public double SpringMaxR => SpringRateMaxR ?? SpringRateMax ?? double.NaN;
    [JsonIgnore] public double RideMinF   => RideHeightMinF ?? RideHeightMin ?? double.NaN;
    [JsonIgnore] public double RideMaxF   => RideHeightMaxF ?? RideHeightMax ?? double.NaN;
    [JsonIgnore] public double RideMinR   => RideHeightMinR ?? RideHeightMin ?? double.NaN;
    [JsonIgnore] public double RideMaxR   => RideHeightMaxR ?? RideHeightMax ?? double.NaN;

    [JsonIgnore] public bool CanComputeSpeeds => RedlineRpm is > 0 && TireDiameter is > 0;
    [JsonIgnore] public bool HasTargetTopSpeed => TargetTopSpeed is > 0;

    /// <summary>legacy overallTireDiameter(): rim + 2×(width×aspect/100)/25.4, in inches,
    /// or null on any non-positive/blank part. width mm, aspect %, rim in.</summary>
    public static double? OverallTireDiameter(double? widthMm, double? aspectPct, double? rimIn)
    {
        if (widthMm is not > 0 || aspectPct is not > 0 || rimIn is not > 0) return null;
        return rimIn.Value + 2 * (widthMm.Value * (aspectPct.Value / 100)) / 25.4;
    }
}
```

The resolved getters use `double.NaN` as the last-resort fallback (no per-axle and no shared
present) deliberately: `readInputs`/fixtures always supply per-axle values, so this never
flows into math; if it ever did, `NaN` surfaces loudly in the sweep rather than silently
clamping wrong. `OverallTireDiameter` reproduces the locked test `315/30R17 → 24.4409`
(legacy/test/unit.test.js:67) and the `null` cases.

---

## 5. Output record graph

`Fh6Tuning.Core\Tune.cs` (Why may live in `Why.cs`). Field names/order mirror the JS `tune`
object leaf-for-leaf so serialized JSON matches `JSON.stringify(compute(...))`. **Every
numeric output field is `double` / `double?`** (resolution §14-2), including braking and
differential — System.Text.Json writes integral doubles without a `.0` suffix, matching JS.

```csharp
namespace Fh6Tuning.Core;

public sealed record Why(string Text, string Formula);

public sealed record SummaryChip(string K, string V);

public sealed record Tires(double Front, double Rear, Why Why);

public sealed record Gearing(
    double Final,
    IReadOnlyList<double> Ratios,
    bool SingleSpeed,
    IReadOnlyList<double>? Speeds,   // null when !CanComputeSpeeds
    double? TopSpeed,                // null when no speeds
    string FdSource,                 // "heuristic" | "target"
    Why Why);

public sealed record Alignment(
    double CamberF, double CamberR, double ToeF, double ToeR, double Caster, Why Why);

public sealed record Arb(double Front, double Rear, Why Why);

// _fFront/_fRear are JS internal scratch (used only by the why string); NOT on the record.
public sealed record Springs(
    double Front, double Rear, double RideF, double RideR, Why Why);

public sealed record Damping(
    double ReboundF, double ReboundR, double BumpF, double BumpR, Why Why);

// Applicable=false → Front/Rear null. Single wing → only its own end populated.
public sealed record Aero(
    bool Applicable,
    double? Front, double? FrontLbf,
    double? Rear, double? RearLbf,
    Why Why);

public sealed record Braking(double Balance, double Pressure, Why Why);

// FWD/RWD: FrontAccel/FrontDecel/CenterRear stay null. AWD populates all five.
// Driveline echoes the input drivetrain as a string ("FWD"/"RWD"/"AWD") — kept as string,
// not the enum, so it round-trips byte-identically and the Web layer switches on it.
public sealed record Differential(
    string Driveline,
    double Accel, double Decel,
    double? FrontAccel, double? FrontDecel, double? CenterRear,
    Why Why);

public sealed record Derived(
    double Frac, double RearFrac,
    double FrontAxle, double RearAxle,
    double FrontCorner, double RearCorner,
    double Pw,
    int PiIdx,
    ClassTier ClassTier,
    double GripFactor,
    bool UndersteerProne, bool OversteerProne,
    bool CanTuneSusp,
    double EvFactor,
    [property: JsonPropertyName("isEV")] bool IsEv);   // legacy key is isEV, not isEv

public sealed record Tune(
    Goal Goal,
    Derived Derived,
    IReadOnlyList<SummaryChip> Summary,   // exactly 5 chips, fixed order
    Tires Tires,
    Gearing Gearing,
    Alignment Alignment,
    Arb Arb,
    Springs Springs,
    Damping Damping,
    Aero Aero,
    Braking Braking,
    Differential Differential);
```

### Summary chip order (locked, legacy/tuning.js:908–914)
`Power-to-weight` (`"{R2(pw)} hp/lb"`), `Front corner` (`"{RInt(frontCorner)} lb"`),
`Rear corner` (`"{RInt(rearCorner)} lb"`), `Balance` (`"{R1(frac*100)}/{R1(rearFrac*100)}"`),
`Class tier` (`d.ClassTier.ToString()` → `"Sports"`/`"HighPerf"`/`"Race"`). All numeric
formatting uses `InvariantCulture`. The `Balance` chip is tested as exactly `"40/60"`
(legacy/test/edge.test.js:71) — confirm `R1(40)` renders `"40"` not `"40.0"` (it does; these
are doubles formatted by interpolation, and `40.0d.ToString(InvariantCulture)` is `"40"`).

> **`derived.classTier` parity:** the legacy summary chip emits `d.classTier` verbatim
> (`"HighPerf"`). `ClassTier.HighPerf.ToString()` is `"HighPerf"` — matches. The
> `unit.test.js` ladder test (`t.derived.classTier === "Race"`, line 304) maps to the enum
> serialized token. Confirmed consistent.

---

## 6. Engine interface, the `derive`+9-categories+dials port, and the neutrality contract

`Fh6Tuning.Core\ITuningEngine.cs` and `TuningEngine.cs`.

```csharp
namespace Fh6Tuning.Core;

public interface ITuningEngine
{
    /// <summary>Pure, deterministic compute(input, goal) → full tune.
    /// At HandlingBias==0 AND OverallStiffness==0 the post-processors are SKIPPED,
    /// returning the per-goal baseline byte-for-byte.</summary>
    Tune Compute(TuneInput input, Goal goal);

    /// <summary>validate(raw) → guard before rendering. Mirrors legacy validate(). See §7.</summary>
    ValidationResult Validate(RawInput input);

    /// <summary>legacy overallTireDiameter — convenience forward to TuneInput.OverallTireDiameter.</summary>
    double? OverallTireDiameter(double? widthMm, double? aspectPct, double? rimIn);

    IReadOnlyList<Goal> Goals { get; }                         // GOALS order
    IReadOnlyDictionary<Goal, GoalMeta> GoalMeta { get; }
}

public sealed record GoalMeta(string Label, string Icon, string Blurb);
public sealed record ValidationResult(bool Valid, IReadOnlyList<string> Errors);
```

`TuningEngine : ITuningEngine` is **stateless and pure**, registered as a **singleton**
(`services.AddSingleton<ITuningEngine, TuningEngine>()`). The parity harness and tests may
construct it directly (`new TuningEngine()`); DI is for the Web layer.

### Engine body structure (1:1 map to legacy/tuning.js)

`Compute` ports lines 903–934. Build the baseline tune from private static pure methods that
mirror the JS functions exactly, in this order and with these signatures (all take the
already-`derive`d `Derived` where the JS does):

| C# method (private static) | legacy fn | lines |
|---|---|---|
| `Derive(TuneInput) → Derived` | `derive` | 54–82 |
| `Tires(in, d, goal) → Tires` | `tires` | 87–127 |
| `Gearing(in, d, goal) → Gearing` | `gearing` | 132–227 (EV branch 158–182) |
| `Alignment(in, d, goal) → Alignment` | `alignment` | 232–293 |
| `Arb(in, d, goal) → Arb` | `arb` | 298–328 |
| `Springs(in, d, goal) → (Springs, double fFront, double fRear)` | `springs` | 333–401 |
| `Damping(in, d, goal, sprFront, sprRear) → Damping` | `damping` | 406–444 |
| `Aero(in, d, goal) → Aero` | `aero` | 449–538 |
| `Braking(in, d, goal) → Braking` | `braking` | 543–573 |
| `Differential(in, d, goal) → Differential` | `differential` + `diffWhy` | 578–641 |

Implementation rules (apply throughout):
1. Use `JsMath` helpers only — never `Math.Round`. `Math.Pow`/`Math.PI`/`Math.Min`/`Math.Max`/
   `Math.Abs` map directly and are bit-identical on the same IEEE-754 inputs.
   `Math.min.apply(null, ratios)` → `ratios.Min()`.
2. Preserve the **exact operation order** from the JS (parenthesization, accumulation order).
3. The JS `{...}[key] ?? fallback` / `[key] != null ? : fallback` lookups have **per-call-site
   fallbacks** (`0`, `0.5`, `3`, `29.0`, `0.7`, `5.0`, etc.). Use `Dictionary.TryGetValue`
   with the documented fallback at each site — do **not** use a blanket `GetValueOrDefault()`.
4. Apply `JsMath.NormZero` at every **signed** rounded output assignment (toe especially —
   `toeF`/`toeR` can emit `-0`; also camber). No-op for positives, so apply defensively.
5. `Springs` returns the ride-frequency scratch (`fFront`,`fRear`) as out/tuple values for the
   why string and the `damping` call — they are **not** serialized (`_fFront`/`_fRear` in JS
   are underscore-private and the parity canonicalizer strips `_`-keys anyway).
6. `Derive` builds `ClassTier` directly (`piIdx<=1 ? Sports : piIdx<=3 ? HighPerf : Race`,
   line 60) and keys `FREQ_BASE`/`MIN_BUMP`/`SUSP_CAP` off the enum.

### The two dials (post-processors) — neutrality contract

JS dials **mutate** the tune in place (`t.arb.front = …`, `why.text += …`). C# records are
immutable, so the post-processors return a **rebuilt** `Tune` via `with`-expressions, touching
only the affected categories and appending to their `Why.Text`.

`Compute` tail (ported from lines 925–932):
```csharp
double stiff = input.OverallStiffness;           // already a double defaulting to 0
if (stiff != 0) tune = ApplyOverallStiffness(tune, input, JsMath.Clamp(stiff, -5, 5));
double bias = input.HandlingBias;
if (bias != 0) tune = ApplyHandlingBias(tune, input, JsMath.Clamp(bias, -5, 5));
return tune;
```
- **Stiffness runs FIRST** (magnitude), then **handling bias** (balance). Order only matters
  at the clamps (the dials are orthogonal).
- **At `0`, each `Apply*` is skipped entirely**, so the baseline `Tune` instance is returned
  unchanged → byte-for-byte identity at 0 holds trivially. This is verified by the sweep
  (`bias0 == baseline`, `stiff0 == baseline`) and by `integration.test.js` / `edge.test.js`.
- `ApplyHandlingBias` ports lines 663–756; `ApplyOverallStiffness` ports lines 776–831.
- `biasScale(b, exp) = sign(b)·(min(|b|,5)/5)^exp` (lines 656–659) → a private static helper.
- Both dials read range bounds from `input` via the resolved accessors (§4) — the same
  per-axle-then-shared `pick` the JS dial bodies use (lines 679–682, 786–799).
- Stock suspension (`!d.CanTuneSusp`) exempts `ApplyOverallStiffness` (returns unchanged,
  line 780) and the ARB/springs levers of `ApplyHandlingBias` (the `d.canTuneSusp` guards,
  lines 667, 677). The differential/braking/aero bias levers still apply.
- `ApplyHandlingBias` leaves Drift's diff accel/decel and brake balance untouched (the
  `isDrift` / `t.goal !== "Drift"` guards, lines 693, 725) — but still moves the AWD center.

---

## 7. `RawInput` + `Validate` — the input contract gate

**Resolution (§14-1): `Validate` takes a raw, loosely-typed `RawInput`, NOT a typed
`TuneInput`.** This is forced by `legacy/test/failure.test.js`, which calls `validate()` with
raw objects containing `weight: "heavy"`, `weight: NaN`, `power: Infinity`, **missing keys**,
and the **shared** `springRateMin/springRateMax` keys — none of which a typed `TuneInput` can
represent. `validate()` runs `Number(i[key])` coercion and checks `typeof number && isFinite`.

`Fh6Tuning.Core\RawInput.cs`. A raw field is "string | number | bool | null/absent", mirroring
what comes off a DOM `.value` (always string) **or** a hand-built test object (number/NaN/
Infinity). Model each field as a small `RawValue` union so JS `Number()` coercion is exact.

```csharp
namespace Fh6Tuning.Core;

/// <summary>A single raw input value as validate() sees it: absent, a string,
/// a double (incl. NaN/±Infinity), or a bool. JsNumber.Coerce(this) == JS Number(v).</summary>
public readonly record struct RawValue
{
    public static readonly RawValue Absent;                 // undefined/null/missing key
    public static RawValue Str(string? s);                 // a DOM .value (may be "")
    public static RawValue Num(double d);                  // a real number (NaN/Inf pass through)
    public static RawValue Bool(bool b);

    public bool IsAbsent { get; }
    // ... discriminator + payload
}

/// <summary>Raw, unparsed engine input — exactly the key set legacy validate() reads,
/// PLUS the shared range keys failure.test.js exercises. Every field is a RawValue so
/// blank/non-numeric/NaN/absent are all representable. The Web layer fills these from
/// the form; the parity/failure tests fill them directly.</summary>
public sealed record RawInput
{
    public RawValue Power { get; init; } = RawValue.Absent;
    public RawValue Weight { get; init; } = RawValue.Absent;
    public RawValue FrontWeightPct { get; init; } = RawValue.Absent;
    public RawValue Torque { get; init; } = RawValue.Absent;
    public RawValue Gears { get; init; } = RawValue.Absent;

    // per-axle ranges
    public RawValue SpringRateMinF { get; init; } = RawValue.Absent;
    public RawValue SpringRateMaxF { get; init; } = RawValue.Absent;
    public RawValue SpringRateMinR { get; init; } = RawValue.Absent;
    public RawValue SpringRateMaxR { get; init; } = RawValue.Absent;
    public RawValue RideHeightMinF { get; init; } = RawValue.Absent;
    public RawValue RideHeightMaxF { get; init; } = RawValue.Absent;
    public RawValue RideHeightMinR { get; init; } = RawValue.Absent;
    public RawValue RideHeightMaxR { get; init; } = RawValue.Absent;
    // shared fallbacks (failure.test.js uses these)
    public RawValue SpringRateMin { get; init; } = RawValue.Absent;
    public RawValue SpringRateMax { get; init; } = RawValue.Absent;
    public RawValue RideHeightMin { get; init; } = RawValue.Absent;
    public RawValue RideHeightMax { get; init; } = RawValue.Absent;
}
```

`Validate` ports `legacy/tuning.js:840–898` verbatim, emitting these **exact** error strings:

| Condition (after `Number()` coercion) | Error string |
|---|---|
| `power`/`weight`/`frontWeightPct` absent/`""`/non-finite | `"Power (hp) is required and must be a number."` / `"Weight (lb) is required and must be a number."` / `"Front weight % is required and must be a number."` |
| `weight <= 0` | `"Weight must be greater than 0."` |
| `frontWeightPct <= 0` | `"Front weight % must be greater than 0."` |
| `frontWeightPct >= 100` | `"Front weight % must be less than 100."` |
| `power < 0` | `"Power cannot be negative."` |
| `torque < 0` (when present & finite) | `"Torque cannot be negative."` |
| `gears` present & non-finite | `"Number of gears must be a number."` |
| `gears < 1` | `"Number of gears must be at least 1."` |
| range `min <= 0` (both ends finite) | `"{Label} min must be greater than 0."` |
| range `max < min` (both ends finite) | `"{Label} max must be greater than or equal to {label-lower} min."` |

Range labels: `"Front spring rate"`, `"Rear spring rate"`, `"Front ride height"`,
`"Rear ride height"`. Each range uses `pick(perAxle, shared)` then `Number()` on both ends and
the `isNum(lo) && isNum(hi)` guard before checking (lines 884–895). The "required" check uses
the exact key-label map at lines 846–850 and the `i[key] === undefined || null || "" ||
!isNum(Number(i[key]))` predicate (line 852).

`failure.test.js` matches errors with **regexes** (`/weight/i`, `/front weight.*less than 100/i`,
`/spring rate max/i`), so the exact strings above satisfy them; `ValidationTests` (§13) also
pins the full strings.

---

## 8. JSON serialization → camelCase JS keys

`Fh6Tuning.Core\Serialization\Fh6JsonContext.cs` (source-gen) + `AeroRangeJsonConverter.cs`.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fh6Tuning.Core.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,   // emit nulls (JS keeps null fields)
    NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(Tune))]
[JsonSerializable(typeof(TuneInput))]
[JsonSerializable(typeof(ValidationResult))]
public partial class Fh6JsonContext : JsonSerializerContext { }
```

CamelCase lowercases only the first char, so the `F`/`R`/`Lbf` suffix fields are correct:
`CamberF`→`camberF`, `ToeR`→`toeR`, `RideF`→`rideF`, `ReboundR`→`reboundR`, `FrontLbf`→
`frontLbf`, `Pw`→`pw`, `FdSource`→`fdSource`, `SingleSpeed`→`singleSpeed`, `CenterRear`→
`centerRear`, `K`/`V`→`k`/`v`, `Text`/`Formula`→`text`/`formula`. The **one** exception is
`IsEv`→`isEv`, but legacy uses `isEV`; pinned with `[JsonPropertyName("isEV")]` on the
`Derived` record (§5).

Negative-zero: handled at the **engine** via `JsMath.NormZero` so records never hold `-0.0`
(no custom `double` converter needed, source-gen stays clean). The parity canonicalizer also
normalizes `-0`→`0` on both sides as belt-and-suspenders (§9).

> **Property order does NOT need to match.** STJ emits in declaration order; JS in insertion
> order. The parity harness compares **parsed JSON trees** (key-sorted), not raw strings — so
> order is irrelevant. This is the single most important harness instruction (§9.3).

The `derived` and `summary` are NOT part of the numeric-parity comparison (the harness drops
them, §9.3) — but the records still serialize so they can be inspected in failure diffs and so
the Web layer can use the same context for copy-to-text/localStorage if convenient.

---

## 9. The JS ↔ C# differential parity harness (the hard contract)

Run **both** engines over the same case grid and diff canonicalized JSON. The oracle is the
**unmodified** `legacy/tuning.js`, executed in Node (v22 on PATH), so there is zero translation
risk. Two pieces: a Node exporter and a C# consumer.

```
parity\                              (committed; regenerated by a tools step)
  cases.json                         ← the full case grid (one JSON array of {id, input, goal})
  jsmath-oracle.json                 ← rounding-primitive table captured from Node
Fh6Tuning.Tests\Parity\
  run-legacy.mjs                     ← Node: requires ../../legacy/tuning.js, computes, emits results
  ParityCaseSource.cs                ← builds the SAME grid in C#; also writes parity/cases.json
  LegacyOracle.cs                    ← spawns `node run-legacy.mjs`, feeds cases.json, caches results
  LegacyInputJson.cs                 ← maps a C# TuneInput → the exact legacy JS input object shape
  JsonCanonicalizer.cs              ← the normalization rules (§9.3), applied to a JsonNode tree
  DifferentialTests.cs              ← [Theory] over every case: parsed-tree equality C# vs legacy
  CanonicalizerSelfCheckTests.cs    ← proves C#/JS number serialization agree before trusting diffs
```

### 9.1 The Node exporter (`run-legacy.mjs`)

The legacy files are browser IIFEs that assign `module.exports` when `module` exists, so Node
`require()` works as-is (`legacy/tuning.js:949`). Read `cases.json`, compute each, emit one
result object per case with the **same canonicalization** the C# side applies:

```js
// Fh6Tuning.Tests/Parity/run-legacy.mjs   (run: node run-legacy.mjs <cases.json> <out.json>)
import { readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
const require = createRequire(import.meta.url);
const { compute } = require("../../legacy/tuning.js");   // path resolved relative to this file
const [, , casesPath, outPath] = process.argv;
const cases = JSON.parse(readFileSync(casesPath, "utf8"));
const out = cases.map(({ id, input, goal }) => ({ id, tune: canonicalize(compute(input, goal)) }));
writeFileSync(outPath, JSON.stringify(out));
// canonicalize() implements the §9.3 rules identically to JsonCanonicalizer.cs
```
The C# side invokes it once per test run (`LegacyOracle` spawns one `node` process, passing the
generated `cases.json`), reads `out.json`, and indexes results by `id`. One spawn, not one per
case — fast and authentic.

### 9.2 The case grid (`ParityCaseSource`)

Mirrors and extends `legacy/sweep.js` so parity covers everything the invariant fuzzer does,
but asserting **equality to the oracle**, not just in-range:
- **The 27-config grid** = `DT(3) × PT(3) × EL(3)` with the per-index derivation from
  sweep.js:34–44 (PI=`PI[idx%8]`, power=`POWERS[idx%3]`, weight=`WEIGHTS[idx%3]`,
  torque=`200+(idx%5)*90`, frontWeightPct=`FWP[idx%3]`, gears=`[4,6,8][idx%3]`,
  tireCompound=`TC[idx%7]`, suspensionType=`SUS[idx%6]`, `hasFrontAero=idx%2===0`,
  `hasRearAero=idx%3!==0`, `aeroInstalled=true`, the fixed range/aero/redline fields).
  (sweep's comment says "81" but the loop yields **27** — reproduce the actual 27.)
- **The 3 canonical cars** from `legacy/test/fixtures.js` (`CAR_LIGHT_RWD`,
  `CAR_HEAVY_AWD_EV`, `CAR_MID_RWD_HIGHPI`) and the `baseInput` default.
- **The `OS` oversteer-prone car** from `unit.test.js:251`.
- For each car × each of 6 goals: dial sweep `bias ∈ {−5,−2.5,0,2.5,5}` ×
  `stiff ∈ {−5,−2.5,0,2.5,5}` plus the 4 `COMBOS` extremes (`[±5,±5]`) — exercises both
  post-processors and the dial-0 neutrality.
- **Edge configs**: EV with `targetTopSpeed` (1.07 headroom), non-EV target back-solve,
  aero-kit variants (front-only / rear-only / none), stock suspension, the asymmetric
  disjoint front/rear range car from `edge.test.js:162`.

`ParityCaseSource` both (a) exposes the cases to xUnit as `[Theory]` member data and (b) has a
generator entry point that writes `parity/cases.json` (run as a one-off `dotnet run`-style tool
or a test that regenerates when missing). `LegacyInputJson` serializes each C# `TuneInput` to
the **exact legacy JS object shape**: enums → legacy strings (`"FWD"`, `"OffRoad"`, `"Offroad"`),
`AeroRange` → `[min,max]` array, `gears` as a number, dials as `handlingBias`/`overallStiffness`
numbers. This mapper is the inverse of the §2 token table and is covered by `EnumNamingTests`.

### 9.3 Canonicalization rules (where parity is won or lost)

Applied **identically** on both sides (Node `canonicalize` and C# `JsonCanonicalizer`) before
comparison:
1. **Drop `why`, `derived`, and `summary`** — these are display strings, not the numeric
   contract. (Why-string parity is checked structurally in xUnit instead, §13.)
2. **Drop null-valued keys** — so JS-omitted `frontAccel` (FWD/RWD) matches C#'s `null`.
3. **Drop `_`-prefixed keys** (`_fFront`/`_fRear`).
4. **Normalize negative zero**: `value == 0.0 ? 0.0 : value`. JS `JSON.stringify(-0)` already
   yields `"0"`; C# must match (engine `NormZero` already ensures this).
5. **Numbers compared by parsed value, not text.** Build a `JsonNode`/`JsonDocument` tree and
   compare numeric leaves as `double` with **exact** equality (tolerance 0). The engine only
   ever emits `JsMath`-quantized values (nice decimals like `4.34`, `1.39`, `29.5`), so the
   shortest-round-trip representations agree; `CanonicalizerSelfCheckTests` proves this on a
   few hundred real engine outputs **before** the differential gate is trusted.
6. **Sort object keys** so declaration-vs-insertion order never causes a false diff.

The diff failure message prints `id`, the JSON path that differs, and both values — pointing
straight at the offending category + input.

### 9.4 `JsMath` micro-parity (`parity/jsmath-oracle.json`)

A focused table captured once from Node (`round`/`r1`/`r2`/`rHalf`/`r5`/`rInt`/`rEven`/`clamp`
over a curated input set incl. the half-rounding cases `0.5,2.5,-0.5,-2.5`, and
`JsNumber.Coerce` cases `""→0`, `"heavy"→NaN`, `" 12 "→12`, `"1e3"→1000`). `JsMathTests` (§13)
asserts the C# helpers reproduce it. This runs first — cheapest signal that rounding matches.

---

## 10. Web layer: services, state, components

`Fh6Tuning.Web`. The **only** project touching DOM/JS/localStorage. No string-built HTML —
Razor + MudBlazor throughout (the legacy `escapeHtml` disappears; Razor auto-escapes).

### 10.1 DI registrations (`Program.cs`)
```csharp
builder.Services.AddMudServices();
builder.Services.AddSingleton<ITuningEngine, TuningEngine>();         // Core engine, pure
builder.Services.AddSingleton<IUnitConverter, UnitConverter>();       // M2I / FIELD_DIM / dp rounding
builder.Services.AddSingleton<INumberFormatter, NumberFormatter>();   // nf / springDisp / rideDisp / aeroDisp / speedDisp
builder.Services.AddSingleton<CardRegistry>();                        // the 9-card + compare defs
builder.Services.AddScoped<ILocalStorage, LocalStorageInterop>();     // JS-interop wrapper
builder.Services.AddScoped<ISetupRepository, SetupRepository>();      // localStorage glue over Core SETUPS
builder.Services.AddScoped<CalculatorState>();                        // live UI state
builder.Services.AddScoped<IClipboard, ClipboardInterop>();          // copy-to-clipboard + fallback
builder.Services.AddScoped<IFileDownload, FileDownloadInterop>();    // export JSON download
```

### 10.2 `UnitConverter` (port of app.js unit logic, lines 11–34, 504–531)
- `M2I` factors verbatim: `weight 2.2046226`, `torque 0.7375621`, `ride 0.3937008`,
  `spring 55.99741`, `aero 2.2046226`, `speed 0.6213712`.
- `ToImp(dim, v)` / `FromImp(dim, v)` (metric → ×factor, imperial → identity).
- `FIELD_DIM` set verbatim (weight, torque, the 4 ride + 4 spring keys, the 4 aero keys,
  targetTopSpeed). **Tire width/aspect/rim are deliberately OUT of `FIELD_DIM`** (CLAUDE.md
  invariant) so the metric toggle never rewrites them — `UnitConverterTests` asserts this.
- `ConvertFieldsInPlace(RawForm, from, to)` ports `setUnits`'s field rewrite with the per-dim
  `dp` rule: `ride→1`, `spring→ metric?2:0`, else `0` (line 513).

### 10.3 `NumberFormatter` (port of app.js display helpers, lines 37–55)
Critical for the "what changed" diff and compare table, which compare **formatted strings**:
- `Nf(double? v, int dp = 1)`: `"—"` for null/NaN; else `v.ToString("F{dp}", Invariant)` then
  strip trailing zeros / trailing `.` (port of the two `.replace` regexes). Uses ASCII `-`
  (do not prettify the minus sign — dial labels build their own sign).
- `SpringDisp/RideDisp/AeroDisp/SpeedDisp` wrap `Nf(FromImp(dim, v), dp)` + unit suffix, with
  the same `dp` and `kgf/lbf`, `km/h/mph`, `cm/in`, `kgf-mm/lb-in` suffix logic as legacy.
- `AeroVal(pct, lbfImp, absent)` ports line 55.

### 10.4 `CalculatorState` (replaces the implicit DOM state)
Plain observable (`event Action? Changed`). Holds `Units`, `CurrentGoal`, `CompareMode`,
`HandlingBias`, `OverallStiffness`, and a string-backed `RawForm` (every form field as a
string, keyed by the legacy element ids). Every `Set*` raises `Changed` once; components
`StateHasChanged()` on it — reproducing the legacy "live updates on every input". `SetUnits`
calls `UnitConverter.ConvertFieldsInPlace` then flips labels (ports lines 504–531). The compute
pipeline (`BuildLiveTune`) ports `refresh()` (lines 466–502):
1. `IsIncomplete()` — **raw-string** emptiness check on `power`/`weight`/`frontWeight`
   (`REQUIRED_FIELDS`, line 449), run **before** parse → Welcome state. (Preserves the
   "incomplete ≠ invalid" first-visit UX from the user's MEMORY.)
2. `ReadInputs()` builds `RawInput` (for `Validate`) and, if valid, a `TuneInput` (metric→
   imperial via `ToImp`, `rangeField`/`optField`/`optAeroRange`/`intField` defaults, aeroKit→
   booleans, `TireDiameter` via `OverallTireDiameter`). Ports lines 84–123.
3. `Engine.Validate(raw)` → errors → Error state.
4. `Live = Engine.Compute(input, goal)`; `Baseline = (dials != 0) ? Compute(input with
   {HandlingBias=0, OverallStiffness=0}) : null` (the "what changed" reference, lines 495–498).

### 10.5 Component tree (`legacy/index.html` + `app.js` → Razor)
```
App.razor → MainLayout.razor
  ├─ MudThemeProvider (Fh6Theme, IsDarkMode=true) + Popover/Dialog/Snackbar providers
  ├─ <TopBar/>                          ← .topbar: brand + unit toggle (MudButton pill group)
  └─ Pages/Calculator.razor  (route "/")
     └─ MudGrid (.layout 2-col)
        ├─ MudItem (left, .panel.inputs, sticky)
        │  └─ <InputsPanel/>
        │     ├─ <SavedSetupsBlock/>           ← fieldset#setupsBlock (§11)
        │     ├─ <IdentityFieldset/>           ← drivetrain/engineLocation/powertrain/piClass selects
        │     ├─ <PerformanceFieldset/>        ← power/torque/weight/frontWeight/gears
        │     ├─ <InstalledPartsFieldset/>     ← compound/suspension + 8 spring/ride range fields
        │     │   └─ <AeroKitFields/>          ← aeroKit select + conditional front/rear lbf range inputs
        │     └─ <GearingRefinementFieldset/>  ← redline/targetTopSpeed + tire width/aspect/rim + computed Ø
        └─ MudItem (right, .panel.outputs)
           └─ <OutputsPanel/>
              ├─ <GoalTabs/>                    ← 6 goal pills (dimmed when compare)
              ├─ <ModeRow/>                     ← compare switch + Copy button
              ├─ <TuningDial Kind="Bias"/> + <TuningDial Kind="Stiff"/>
              ├─ <SliderChangesPanel/>          ← "What the sliders changed"
              ├─ <SummaryStrip/>                ← 5 derived chips
              ├─ <OutputCards/>                 ← 9 cards OR <WelcomeCard/> OR <ErrorCard/>
              │   └─ <TuneCard/> ×9 (+ <WhyExpander/>)
              └─ <CompareTable/>                ← all-goals table (compare mode only)
```

### 10.6 `CardRegistry` — one declarative table for cards AND compare
The legacy `CARDS` (lines 190–217) and `compareRowDefs` (lines 313–372) are the same data two
ways. Collapse into one typed registry so they never drift:
```csharp
public sealed record TuneRow(string Key, string Value, bool Sub = false);
public sealed record CardDef(string Id, string Title, string Icon,
    Func<Tune, FormatContext, IReadOnlyList<TuneRow>> Rows, Func<Tune, Why?> Why);
```
`FormatContext` carries `UnitSystem` + the input flags the compare table branches on
(`ev`, `redline && tireDiameter` → top-speed row, `hasFrontAero && aeroFront.Min != null` →
lbf vs % header, `drivetrain == AWD` → 5 diff rows vs 2). Direct port of the `ev`/`aeroFrontLbf`/
`aeroRearLbf` locals in `compareRowDefs`. `gearRows`/`diffRows` port lines 156–188.

### 10.7 `TuningDial`, dials labels, changes panel
- `TuningDial`: `MudSlider<double>` `Min=-5 Max=5 Step="0.5"`, themed track gradient by `Kind`,
  live label porting `updateBiasLabel`/`updateStiffLabel` (lines 134–153) — `"Neutral (0)"` /
  `"Understeer (−2)"` / `"Oversteer (+3)"`, `"Balanced (0)"` / `"Soft"/"Hard"`, with the
  `under/over/soft/hard` color classes and `+`/`−` sign + `Nf(v,1)`. Reset button → 0.
- `SliderChangesPanel`: ports `effectPhrase` + `renderChangesPanel` (lines 248–310) — diffs
  `Live` vs `Baseline` by comparing the formatted `CardRegistry` row strings, emits the
  per-card effect phrases (arb/springs/damping/braking/differential/aero/default switch),
  header counts moved settings + lists active dials. Inline `row.changed` highlight = same diff
  in `TuneCard`.
- `OutputCards` 3-way switch ports `refresh()`'s welcome/error/tune states (incomplete→Welcome,
  invalid→Error, valid→cards or compare).
- `CopyTune`: a pure `TuneToText` port (lines 387–412), lives in Web (depends only on the
  formatter); `IClipboard.WriteText` with a `MudDialog` fallback replacing `window.prompt`.

---

## 11. localStorage + saved setups

### 11.1 Pure storage logic in Core (`Fh6Tuning.Core\Setups\`)
Port `legacy/setups.js` 1:1 — pure, **no** localStorage access of its own (same separation as
legacy). `SetupStore.cs`:
```csharp
namespace Fh6Tuning.Core.Setups;

public const string StorageKey = "fh6-tuning.setups.v1";   // STORAGE_KEY
public const int Schema = 1;                                 // SCHEMA

public sealed record Setup {                                 // a stored entry
    public required string Name { get; init; }
    public string SavedAt { get; init; } = "";
    public string Units { get; init; } = "imperial";        // "imperial"|"metric"
    public string Goal { get; init; } = "";
    public IReadOnlyDictionary<string, string> Dials { get; init; } = ...; // handlingBias/overallStiffness as strings
    public IReadOnlyDictionary<string, string> Fields { get; init; } = ...; // raw field strings by element id
    public IReadOnlyDictionary<string, JsonElement> Extra { get; init; } = ...; // unknown keys preserved (fwd-compat)
}
public sealed record SetupDb(int Schema, IReadOnlyList<Setup> Setups);
public sealed record ParseResult(bool Ok, SetupDb? Db, int Skipped, string? Error);
public sealed record MergeResult(SetupDb Db, int Added, int Updated);

public static class SetupStore {
    public static SetupDb EmptyDb();                          // {schema:1, setups:[]}
    public static Setup? ValidateSetup(JsonElement obj);      // null if no usable name/fields object
    public static ParseResult ParseDb(string? json);          // garbage→{ok:false}; future schema read entry-by-entry; dedupe by trimmed name (last wins)
    public static string SerializeDb(SetupDb db);             // pretty (2-space) JSON, like JSON.stringify(db,null,2)
    public static SetupDb UpsertSetup(SetupDb db, Setup setup);
    public static SetupDb DeleteSetup(SetupDb db, string? name);
    public static MergeResult MergeDb(SetupDb existing, SetupDb imported);
}
```
Parity notes the `SetupStoreTests` (port of `setups.test.js`) enforce:
- `ValidateSetup` requires a non-empty **trimmed** string name and an object (not array)
  `fields`; trims the name; defaults `units` (`"metric"` only if exactly `"metric"`, else
  `"imperial"`), `goal`→`""`, `dials`→`{}`, `savedAt`→`""`; **preserves unknown extra keys**.
- The **`__proto__` neutralization** (setups.js:29): when reading raw JSON, a smuggled
  `__proto__` key must not pollute the prototype. In C#, parse with `JsonDocument`/`JsonElement`
  (which treats `__proto__` as an ordinary property name) and copy known + extra keys explicitly
  — there is no prototype to poison, so this is naturally safe; `SetupStoreTests` still asserts
  no `evil` key leaks and the entry is a plain object.
- `ParseDb` rejects: non-string input, invalid JSON, non-object/array envelope, `schema` not a
  number or `< 1`, missing/`non-array` `setups`. A **future** schema (`2`) is read entry-by-
  entry and normalized to `schema: 1`. Invalid entries dropped + counted in `skipped`; same-
  name (trimmed) entries deduped (last wins) and counted.
- `SerializeDb` must match `JSON.stringify(db, null, 2)` formatting (2-space indent) so a
  round-trip and the export file are byte-identical to legacy. Use
  `JsonSerializerOptions { WriteIndented = true }` (4-space is the STJ default — **override to
  2 spaces** via `IndentSize = 2` / `IndentCharacter = ' '`, available in .NET 9+). A
  `SetupStoreTests` golden asserts the exact text.
- `Upsert/Delete/Merge` are pure (return new dbs, never mutate input — `setups.test.js:152`
  asserts non-mutation).

### 11.2 Web glue
- `ILocalStorage` wraps a tiny JS ES module (`wwwroot/js/localStorageInterop.js`) with
  try/catch get/set (private-mode safe), exposed as async `GetAsync`/`SetAsync`.
- `SetupRepository` composes `SetupStore` + `ILocalStorage`, porting `loadSetupsDb`/
  `saveSetupsDb` incl. the degrade-to-empty + status messages (`"Browser storage unavailable
  — setups won't persist."`, `"Stored setups were unreadable — starting fresh."`, the dropped-
  count message) and `lastLoadSkipped`.
- `SavedSetupsBlock` maps every control (lines 30–47, 640–706): name field + Save (overwrite
  confirm via `MudDialog` replacing `window.confirm`), `MudSelect` (sorted by `savedAt` desc) +
  Load/Delete/Export (disabled when empty), Export via `IFileDownload` (filename
  `fh6-setups-YYYY-MM-DD.json`), Import via `MudFileUpload` → `ParseDb`→`MergeDb`→save, the
  auto-clear-after-4s status line, `snapshotSetup`/`applySetup` ordering (`SetUnits` first,
  then overwrite fields, absent dial keys → `"0"`).

---

## 12. Dark theme (MudThemeProvider + minimal scoped CSS)

`Fh6Tuning.Web\Theme\Fh6Theme.cs` maps `legacy/styles.css` `:root` palette to a `MudTheme`:
```csharp
public static readonly MudTheme Fh6Theme = new() {
  PaletteDark = new PaletteDark {
    Black = "#0d1117", Background = "#0d1117", Surface = "#161d28",
    AppbarBackground = "#141c28", DrawerBackground = "#131a24",
    Primary = "#3ddc84",        // --accent  (Forza green)
    Secondary = "#46a7ff",      // --accent-2 (electric blue)
    Warning = "#ffb454",        // --warn
    Error = "#ff6b6b",          // --danger
    TextPrimary = "#e6edf3", TextSecondary = "#8b97a7",
    Lines = "#263246", Divider = "#1e2836",
  },
  LayoutProperties = new() { DefaultBorderRadius = "12px" }
};
```
`<MudThemeProvider Theme="Fh6Theme" IsDarkMode="true"/>`. Pieces MudBlazor tokens can't express
stay as a small `wwwroot/css/app.css` (CSS variables `--accent` etc. + body radial-gradient
backdrop + the two slider-track gradients + `.row.changed`/`.slider-changes` accents + the
`details.why` monospace formula block + the compare sticky-first-column table + the
`@media 880px/480px` breakpoints) ported verbatim. Per-component bespoke bits use scoped
`Foo.razor.css` (e.g. `TuningDial.razor.css` with `::deep` for the slider gradient by `Kind`).

---

## 13. xUnit test file plan (maps every `legacy/test/*` file)

```
Fh6Tuning.Tests\
├─ Fixtures\
│  ├─ CanonicalCars.cs        ← baseInput + CAR_LIGHT_RWD / CAR_HEAVY_AWD_EV / CAR_MID_RWD_HIGHPI / OS (port of fixtures.js as TuneInput builders)
│  └─ Ranges.cs               ← RANGES table
├─ Engine\
│  ├─ JsMathTests.cs          ← round/r1/r2/rHalf/r5/rInt/rEven/clamp + JsNumber.Coerce vs parity/jsmath-oracle.json    [primitives]
│  ├─ UnitValueTests.cs       ← EXACT locked values for L/E/M Circuit + overallTireDiameter + R-class ladder + oversteer-prone OS   [port unit.test.js]
│  ├─ DirectionalTests.cs     ← goal directions, all-goals-distinct, both dials ordered/neutral/compose, stock exemptions, gearing back-solve   [port integration.test.js]
│  ├─ EdgeTests.cs            ← FWD/RWD/AWD diff field shape, EV single-speed + 1.07 headroom, rear-engine inversion, aero suppression/single-wing/Drag-zero, aero lbf map, bias-0 identity per goal, stock locks, asymmetric disjoint ranges   [port edge.test.js]
│  ├─ RangeInvariantTests.cs  ← AssertAllInRange / AssertSpringInPart / AssertWhyShape helpers (port helpers.js)
│  ├─ WhyShapeTests.cs        ← every section has non-empty {Text, Formula}
│  └─ SweepTests.cs           ← the 27×6×dial grid invariants as [Theory]: no NaN, in-range, gears strictly descending, 6 goals distinct, drivetrains distinct, dial-0 + stiff-0 neutrality   [port sweep.js]
├─ Validation\
│  └─ ValidationTests.cs      ← Validate(RawInput) contract: exact error strings + regex matchers; power=0 allowed; compute-never-throws-and-clamps   [port failure.test.js]
├─ Storage\
│  └─ SetupStoreTests.cs      ← EmptyDb/ValidateSetup/ParseDb/SerializeDb round-trip/Upsert/Delete/MergeDb, __proto__ neutralization, dedupe, future-schema, non-mutation, 2-space golden   [port setups.test.js]
├─ Web\
│  ├─ UnitConverterTests.cs   ← M2I round-trips, FIELD_DIM coverage, tire-size stays unconverted, per-dim dp rounding (mirrors setUnits)
│  ├─ NumberFormatterTests.cs ← Nf/springDisp/rideDisp/aeroDisp/speedDisp vs a JS toFixed+trim golden table
│  ├─ ReadInputsTests.cs      ← RawForm → RawInput + TuneInput: metric→imperial, optField/rangeField/intField defaults, aeroKit→flags, tireDiameter
│  ├─ TuneToTextTests.cs      ← copy-text golden strings for the 3 cars (captured from app.js)
│  └─ ChangesPanelTests.cs    ← effectPhrase + live-vs-baseline diff phrases/dirs
└─ Parity\
   ├─ DifferentialTests.cs        ← [Theory] full-tune C#==legacy over the whole grid — THE byte-for-byte gate
   ├─ EnumNamingTests.cs          ← every enum value → exact legacy string (the §2 golden table)
   └─ CanonicalizerSelfCheckTests.cs ← C# vs JS number serialization agree (run before the gate)
```

Exact locked values to pin in `UnitValueTests` (from `unit.test.js`, captured here so the
implementer doesn't re-derive): tires.front L/E/M = `29.5/33.5/32`; tires.rear = `29/33/31`;
gearing.final = `4.34/3.89/3.61`; L.ratios = `[3.21,2.05,1.57,1.3,1.13,1]`; E.ratios = `[1.39]`;
M.ratios = `[2.88,1.83,1.41,1.17,1.01,0.9,0.81]`; camberF = `-2.1/-2.3/-2.3`; camberR =
`-0.9/-1/-1.2`; toeF all `0` (after `NormZero`); toeR = `0.1/0/0.2`; caster = `5.2/7/6.4`;
arb.front = `15.94/26.04/11.29`; arb.rear = `12.94/27.33/13.4`; springs.front = `379/900/676`;
springs.rear = `240/900/786`; rideF = `4.5/4.6/4.5`; rideR = `4.5/4.8/4.6`; reboundF =
`9/10.8/9.1`; reboundR = `8.7/10.6/9.5`; bumpF = `5.4/6.4/5.5`; bumpR = `5.3/6.3/6`; aero
(E) front/rear = `100/15`, (M) = `95/90`, (L) not applicable; braking.balance = `54/57/48`;
braking.pressure = `105/120/110`; diff (L RWD) accel/decel = `56/20`, (M RWD) = `38/20`,
(E AWD) accel/decel/frontAccel/frontDecel/centerRear = `72/30/26/5/83`; `overallTireDiameter`
`315/30/17→24.4409`, `225/45/17→24.9724`, `245/40/19→26.7165`, null cases.

Component rendering (bUnit) is out of scope: the contract is numeric and the components are thin
glue over tested services. May be added later but is not required by any legacy contract.

---

## 14. Resolved decisions (where the two proposals disagreed)

1. **`Validate` takes `RawInput` (raw, loosely-typed), not `TuneInput`.** *Decisive evidence:*
   `legacy/test/failure.test.js` calls `validate()` with `weight:"heavy"`, `weight:NaN`,
   `power:Infinity`, **missing keys**, and the **shared** `springRateMin/Max` keys (lines
   54–98). A typed `TuneInput` literally cannot carry these states. Proposal A flagged this as
   an open question and leaned typed; Proposal B specified raw. **B is correct.** The Web layer
   builds `RawInput` from the form for `Validate`, and a separate parse step produces the typed
   `TuneInput` for `Compute`. The "required & must be a number" strings are emitted by
   `Validate` (it sees the raw absence), not the Web layer.

2. **All output numerics are `double` (incl. braking/differential), not `int`.** Proposal A
   said `double`; B used `int` for `Braking`/`Differential`. The engine produces these via
   `rInt`/`rEven`/`r5`/`clamp` which are `double` ops (legacy/tuning.js:555,562,598,617–619),
   the dials do further `double` arithmetic on them (lines 700–727), and JSON parity compares
   numeric values regardless of CLR type. Uniform `double` avoids int/double conversion seams
   in the dial post-processors and the `centerRear ?? "—"` formatting. The `accel % 2 == 0`
   even-check in `unit.test.js:230` holds for integral doubles. **A is correct.**

3. **`Powertrain` member order = `ICE, EV, Hybrid`; `AeroKit` = `None, Front, Rear, Full`.**
   Order is behaviorally irrelevant (enums are string-keyed in the engine), but fixed here so
   later agents don't diverge. (A had `ICE,Hybrid,EV` / `None,Front,Rear,Full`; B had
   `ICE,EV,Hybrid` / `Full,Front,Rear,None`. Chosen: B's powertrain order, A's aerokit order —
   arbitrary tie-breaks.)

4. **`Gears` is `double` on `TuneInput`** (not `int`). The engine re-applies
   `clamp(Math.round(i.gears), 2, 10)` (line 192), so carrying a `double` is faithful and the
   Web `intField` default (`6`) flows through; an `int` would pre-empt the engine's own round.

5. **Parity comparison is parsed-tree, key-sorted, exact-numeric** — never raw-string. Both
   proposals converged here; pinned as §9.3 rule 5/6 because STJ declaration order ≠ JS
   insertion order.

6. **Negative-zero handled in the engine (`JsMath.NormZero`)**, not via a custom STJ `double`
   converter — keeps source-gen clean. The canonicalizer also normalizes as a backstop.

7. **`Springs._fFront/_fRear` are NOT on the record** — returned as engine-internal tuple
   values for the why string + the springs→damping handoff; the parity canonicalizer strips
   `_`-keys, so they never participate in parity.

8. **Engine is a DI-registered singleton implementing `ITuningEngine`** (not a `static` class).
   Satisfies "engine pure & DI-registered"; tests/harness can still `new TuningEngine()`. (A
   proposed an interface+singleton; B proposed `static` + a forwarding facade. The singleton
   `ITuningEngine` is simpler and equally pure — chosen.)

---

## 15. GitHub Pages publish pipeline

Legacy is the current live site (`smadam813.github.io/fh6-tuning-calculator`, per user MEMORY).
The Blazor build publishes to static files served under the project sub-path:
1. `dotnet publish Fh6Tuning.Web -c Release -o publish` → `publish/wwwroot` is the static site.
2. **Base href**: rewrite `<base href="/" />` → `<base href="/fh6-tuning-calculator/" />`
   (post-publish step or `--base-href` at publish).
3. **`.nojekyll`**: add empty `wwwroot/.nojekyll` so the `_framework` underscore folder isn't
   stripped by Jekyll.
4. **SPA 404 fallback**: copy `index.html` → `404.html` for deep-link/refresh safety.
5. `.github/workflows/pages.yml` runs publish + rewrite + uploads the Pages artifact. The
   legacy site remains the in-repo parity oracle; the deployed artifact is the Blazor build.

---

## 16. Build / verify order for implementers

1. Scaffold the solution (§0). Delete the blazorwasm sample pages and the classlib stub.
2. Land Core: `JsMath`, `JsNumber`, `Enums`, `AeroRange`, `TuneInput`, `Tune`/records,
   `RawInput`/`RawValue`, `Validate`, `Serialization`, then the engine body (`TuningEngine`)
   and `SetupStore`.
3. Green `JsMathTests` + `UnitValueTests` first (cheapest signal that rounding + key formulas
   match).
4. Green `CanonicalizerSelfCheckTests`, then **`DifferentialTests`** — the byte-for-byte gate;
   everything else corroborates.
5. Green `SweepTests`, `EdgeTests`, `DirectionalTests`, `ValidationTests`, `SetupStoreTests`.
6. Land Web services (formatter/converter/readInputs/cardRegistry/tuneToText) + their unit
   tests, then components, then localStorage + setups.
7. `dotnet publish` + the Pages rewrite; smoke-test under `/fh6-tuning-calculator/`.
```
