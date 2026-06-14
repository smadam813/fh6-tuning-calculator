# FH6 Tuning Calculator

A single-page app that turns a car's known stats into a complete, math-derived
Forza Horizon 6 tune across **every** tuning category — with the formula and reasoning
shown beneath each output, and live side-by-side comparison of all six tuning goals.

Built as a **standalone .NET 10 Blazor WebAssembly** app with a MudBlazor UI. The tuning
math is a pure, unit-tested C# library; the published output is a fully static bundle that
runs on any web host (e.g. GitHub Pages) with no server-side code.

> The original zero-dependency JavaScript app is preserved under [`legacy/`](legacy/). It is
> not dead code — it is the **parity oracle**: the C# engine is tested to reproduce the legacy
> engine's output byte-for-byte (see [Verification](#verification)).

## Projects

```
Fh6Tuning.Core        the engine + storage — pure compute(input, goal) -> tune, no IO, no Blazor
Fh6Tuning.Web         Blazor WASM + MudBlazor UI — the only project that touches DOM/localStorage/JS
Fh6Tuning.Tests       xUnit: differential parity gate, invariant sweep, unit/edge/storage tests
Fh6Tuning.Web.Tests   xUnit: the pure Web services (unit conversion, output formatting)
legacy/               the original JS app — preserved as the parity oracle
parity/               generated parity snapshot (cases.json, git-ignored, reproduced from legacy/)
research/             the sourced formulas this engine is built on (provenance)
```

`Fh6Tuning.Core` is a pure class library: the `TuningEngine` (`Compute` / `Validate` /
`OverallTireDiameter`) and `SetupsStore` (saved-setup validate/merge/serialize) have no IO and
no Blazor dependency, so they are unit-testable in isolation. `Fh6Tuning.Web` adds the UI,
unit conversion, `localStorage`, clipboard, and file-download interop on top.

## Build, test, run, publish

Requires the **.NET 10 SDK**. Running the tests also requires **Node.js** on PATH (the parity
gate regenerates its snapshot from the legacy JS engine). All commands run from the repo root.

```bash
dotnet build Fh6Tuning.sln              # build everything
dotnet test  Fh6Tuning.sln              # run all xUnit tests (parity, sweep, unit, web services)
dotnet run   --project Fh6Tuning.Web    # dev server at http://localhost:5221
```

**Publish for GitHub Pages** — publish the Web project and deploy the WASM app's static `wwwroot`:

```bash
dotnet publish Fh6Tuning.Web -c Release -o publish
# the complete static site is at:  publish/wwwroot
```

When deploying to GitHub Pages: add a `.nojekyll` file (so the `_framework` runtime folder is
served), copy `index.html` to `404.html` for SPA deep-link fallback, and if you host under a
project sub-path set the app's `<base href>` to match (publish with
`--base-href /your-repo-name/`).

## What it does

**Inputs** — drivetrain, engine location, powertrain, PI class, power, torque, weight,
front-weight %, gear count, tire compound, suspension type, aero, and the ride-height /
spring-rate ranges read off the car's installed parts. Imperial **or** metric. The numeric
fields **start blank** — on first visit you get a short welcome prompt rather than a tune,
and the calculator only needs power, weight and front-weight % to get going (the part-range
fields fall back to the placeholder defaults shown if left empty).

**Outputs** (per goal, or all goals side-by-side):
Tires (F/R psi) · Gearing (final drive + every gear ratio) · Alignment (camber F/R,
toe F/R, caster) · Anti-roll bars (F/R) · Springs (F/R rate + ride height F/R) ·
Damping (rebound F/R, bump F/R) · Aero (F/R downforce) · Braking (balance, pressure) ·
Differential (accel/decel lock, per driven axle + AWD center split).

Every value is a concrete number, clamped to the legal Forza slider range, and adapts
meaningfully across the six **goals**: Circuit · Drag · Drift · Off-Road · Rally · Touge.

## Tuning dials

Two −5…+5 dials reshape the recommended tune on top of the per-goal baseline. They're
**orthogonal** — one changes *balance*, the other changes *firmness*:

- **Handling Bias** (Understeer ↔ Oversteer) — a *balance* knob. Moves each end the
  **opposite** way (soften front bar / stiffen rear, shift brakes & aero, tighten the diff)
  to dial in more front grip or freer rotation **without** changing how firm the car is.
- **Overall Stiffness** (Soft ↔ Hard) — a *firmness* knob. Scales springs, anti-roll bars
  and damping the **same** way at both ends (and drops/raises ride height) to make the whole
  car harder or softer **without** changing its balance.

At **0**, each dial is a true no-op — the tune is returned byte-for-byte as the pure
per-goal baseline (a hard contract verified by the test sweep). Stock suspension exempts the
stiffness dial (nothing to firm), and Drift/Drag exempt the bias levers that define their
character.

**What the sliders changed.** Whenever a dial is off-center, a panel lists every setting that
moved versus the centered baseline — in plain language ("stiffer front anti-roll bar",
"more front brake bias") with the `before → after` value — and the affected rows in the
output cards are highlighted in place with a `was X` marker. So when you nudge a dial to chase,
say, a little more oversteer, you can see exactly *what all just changed* and by how much.

## Saved setups

Save the whole Car Setup panel — plus both dials and the selected goal — under a
name, right in your browser (localStorage; no account, no server). Load or delete
saved setups from the dropdown at the top of the panel. **Export JSON** downloads
the whole collection as a backup file; **Import JSON** restores one, merging by
name (the file wins on conflicts — everything else you've saved stays put).
Setups store the raw field text plus the units they were entered in, so blanks
stay blank and metric setups reload exactly as typed. The storage *logic* lives in
`Fh6Tuning.Core` (`SetupsStore`, pure); only the actual `localStorage` access lives in
the Web layer.

## The formulas (and why)

These are the community-canonical Forza tuning formulas (consistent across FH4/FH5/FH6),
cross-referenced across multiple guides and reconciled by an adversarial review pass.
Full derivations, per-goal modifier tables and edge cases live in [`research/`](research/).

| Category | Core formula |
|---|---|
| **Springs** | Ride-frequency model: `K = f² · W_corner / 9.78`, where `f` (Hz) is a target ride frequency by class + aero + goal, and `W_corner` is the static load on that corner. Clamped to the part's lb/in range, with a support-floor so the car can't bottom out. |
| **Ride height** | Fraction of the part's min↔max range per goal (Circuit = min for low CoG, Off-Road = max, Drag = forward rake), plus compound/aero/anti-bottoming nudges. |
| **Damping** | `Bump = MinBump(class) + frontAxle/200 × 0.1`; `Rebound = Bump / 0.6`. Rear offset by front/rear spring-rate gap; per-goal stiffness multipliers; bump held to 40–70% of rebound (bypassed for off-road/rally/drag-launch). |
| **Gearing** | `FinalDrive = 4.25 + (400 − hp)/600` (+ weight, goal, aero, drivetrain, engine-location terms). Gear ratios follow the power series `Rₙ = A · nᴮ` with first gear `A` from power-to-weight and spacing exponent `B ≈ −0.65` (wider for off-road, tighter for drag). EVs are single-speed: the lone "1st" ratio is shown alongside the final drive (both are in-game sliders and only correct as a pair), and target-top-speed solves gear the limiter ~7% past the target because FH6 EV motors lose power near redline. |
| **Alignment** | Front camber from a tire-grip factor (−0.6° rally rubber → −2.0° slicks), shifted by drivetrain and front load; rear ≈ 55% of front. Caster scales with weight + PI class. Toe near zero except goal-driven toe-out (drift/turn-in) and rear toe-in (RWD stability). |
| **Anti-roll bars** | ForzaFire base `(weight/2) / (200 − 200 · classStiffness%)`, split front/rear by the weight-distribution deviation (RWD ±1 / AWD ±0.66 / FWD ∓1 per 1%), then per-goal multipliers (e.g. drift = soft front / very stiff rear). |
| **Aero** | Overall downforce *level* per goal × an aero-*balance* (front share) per drivetrain, expressed as % of each wing's slider range. AWD circuit forces max-front/min-rear; drag floors both. **Single-wing cars** (front-splitter-only or rear-wing-only) can't be rebalanced, so the one wing you have is sized to the car's existing tendency — e.g. a rear-wing-only car that already understeers keeps the wing low instead of maxing it. |
| **Braking** | Balance = drivetrain base (RWD 52 / AWD 54 / FWD 58) + ½% per 1% front weight + goal. Pressure = 100 + mass + compound + goal. |
| **Differential** | Per-axle base + a power-to-weight trim (high power loosens accel lock) + per-goal adjust. FWD front-only (accel capped 95), RWD rear-only, AWD all three with a center split that's floored at 50% rear. |

Each card in the app shows its own formula and a one-line rationale under **“Why & formula.”**

## Edge cases handled

FWD vs RWD vs AWD (diff routing, ARB/camber/brake bias inversion) · EV (single-speed,
heavier-body spring/damper trims, regen brake bias by drive axle) vs ICE vs Hybrid ·
front/mid/rear engine weight bias · aero kit (full / front-splitter-only / rear-wing-only /
none — single-wing setups sized to the car's balance tendency since you can't offset with
the missing end) · tire-compound grip · stock suspension (alignment/ARB locked) ·
soft-by-design goals kept soft instead of slammed to max.

## Sources

forza.guide · ForzaTune · ForzaFire · grindout · skycoach · vpesports · gamingpromax ·
Nexus Mods · plus community build-theory threads and tuning spreadsheets. There is no
official Forza tuning formula — all math is community-standard, validated through testing,
and these values are **starting points**: fine-tune from there with in-game telemetry.

## Verification

The C# engine is held to **byte-for-byte parity** with the legacy JS engine. `legacy/parity-export.js`
runs the legacy `compute()` over a deterministic grid — the 27-config `DT × PT × EL` matrix plus the
3 canonical fixture cars, across all six goals, swept over both dials — and writes the expected
results to `parity/cases.json` (2340 cases, regenerated on demand, never committed). `Fh6Tuning.Tests`
replays every case through the C# `TuningEngine` and asserts an exact match on every numeric leaf
(bit-for-bit after `-0` normalization), every boolean leaf, and every summary chip; only the `why`
prose strings are a separate soft category so wording drift can't mask a math regression. To stay
bit-identical to JavaScript's `Number` semantics, all Core math is `double` and all rounding goes
through `JsMath` (JS `Math.round` = half toward +Infinity), never `Math.Round`.

On top of parity, `SweepTests` (the xUnit equivalent of `legacy/sweep.js`) fuzzes the full input space
and asserts the invariants regardless of exact values: zero crashes, every output within its legal
slider range, gear ratios strictly descending, all six goals distinct per car, and dial-0 baseline
neutrality.
