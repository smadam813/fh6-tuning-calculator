## What this is

A **standalone .NET 10 Blazor WebAssembly** single-page app that converts a car's known stats into a complete Forza Horizon 6 tune across every tuning category, for six driving goals (Circuit · Drag · Drift · Off-Road · Rally · Touge). It is a 1:1 port of the original zero-dependency JS app (now preserved under `legacy/`), with the pure tuning math moved into a testable C# class library and a MudBlazor UI. The app publishes to a fully static bundle that runs from any web host (GitHub Pages) with no server-side code.

> **The original JS app lives in `legacy/`.** It is not dead code: it is the **parity oracle** for the C# port. Every formula change starts there, and the legacy engine generates the expected snapshot the C# engine is tested against (see the parity harness below).

## Architecture

Three shipping projects plus a Web-services test project, kept deliberately layered so the engine and storage stay pure and unit-testable while all DOM/IO/JS-interop lives in the Web project:

| Project | Role | Key surface |
|---|---|---|
| `Fh6Tuning.Core` | **The engine + storage.** Pure, no IO, no Blazor. `net10.0` class library. | `ITuningEngine` / `TuningEngine` (`Compute(TuneInput, Goal) → Tune`, `Validate`, `OverallTireDiameter`, `Goals`, `GoalMeta`); `SetupsStore` (pure validate/merge/serialize/parse for saved setups); the `Tune`/`TuneInput`/`SavedSetup` record graph; `JsMath`/`JsNumber` (JS-parity numerics). |
| `Fh6Tuning.Web` | **The UI.** Blazor WASM (`Microsoft.NET.Sdk.BlazorWebAssembly`) + MudBlazor. The only project that touches the DOM, `localStorage`, the clipboard, or JS interop. | `Program.cs` (DI wiring), Razor components under `Components/`, page shells under `Pages/`/`Layout/`, and the `Services/` layer (`CalculatorState`, `UnitService`, `TuneFormatter`, `SetupsStorage`, `ClipboardInterop`, `FileDownloadInterop`). |
| `Fh6Tuning.Tests` | **xUnit engine + storage tests.** References `Fh6Tuning.Core`. | The differential **parity** gate (`ParityTests` + `ParityHarness`), the invariant `SweepTests`, plus `UnitTests`/`EdgeTests`/`FailureTests`/`IntegrationTests`/`SetupsTests` with shared `Fixtures`/`Helpers`. |
| `Fh6Tuning.Web.Tests` | **xUnit Web-services tests.** References Core **and** Web; exercises the pure services that carry no Blazor-runtime dependency. | `UnitServiceTests`, `TuneFormatterTests`. |

All four are in `Fh6Tuning.sln`.

**Purity contract.** `Fh6Tuning.Core` has zero IO and zero Blazor dependencies. `TuningEngine` is pure and deterministic and is registered as a DI **singleton**; `SetupsStore` is a static class of pure transforms with **no `localStorage` access of its own** — the actual `localStorage` get/set lives in `Fh6Tuning.Web/Services/SetupsStorage.cs`, exactly mirroring the legacy split between `setups.js` (logic) and `app.js` (IO).

**JS byte-for-byte parity is the hard contract for the engine.** `TuningEngine` is a line-referenced port of `legacy/tuning.js`. To keep it bit-identical to the JS `Number` semantics:
- **All math is `double`** (IEEE-754, == JS `Number`) — never `decimal`, never `float`.
- **Rounding goes through `JsMath`, never `Math.Round`.** `JsMath.Round = Math.Floor(x + 0.5)` reproduces JS `Math.round` (half toward +Infinity); `Math.Round` (banker's rounding) is forbidden in Core. `JsMath` also holds `R1`/`R2`/`RHalf`/`R5`/`RInt`/`REven`/`Clamp`.
- **Signed rounded outputs pass through `JsMath.NormZero`** because `JSON.stringify(-0) === "0"` in JS but `System.Text.Json` writes `"-0"`.
- Operation order, parenthesization, and per-call-site fallbacks match the JS exactly.

**Imperial is the engine's only unit system.** `Fh6Tuning.Core` does all math in lb / in / lb/in / hp / lb-ft. `Fh6Tuning.Web/Services/UnitService.cs` converts metric input → imperial before calling `Compute`, and imperial → metric for display (port of the legacy `M2I`/`FIELD_DIM`/display formatters in `app.js`). Tire width/aspect/rim are unit-independent in Forza and are deliberately kept out of the metric conversion so the unit toggle never rewrites them. Keep all unit logic in the Web layer; never introduce metric into Core.

**`Compute` flow** (`TuningEngine`): `Derive(input)` computes shared quantities (corner loads, class tier, etc.) once, then per-category methods (`tires`, `gearing`, `alignment`, `arb`, `springs`, `damping`, `aero`, `braking`, `differential`) build the `Tune`. Each category returns its numbers plus a `Why { Text, Formula }` shown in the UI. Every numeric output is **clamped to its legal Forza slider range**.

**The two dials are post-processors with a hard neutrality contract.** After the baseline tune is built, overall-stiffness (firmness/magnitude) runs, then handling-bias (front/rear balance). **At `HandlingBias == 0` AND `OverallStiffness == 0`, both post-processors are skipped, so `Compute` returns the per-goal baseline byte-for-byte** — an invariant `SweepTests` verifies. If you touch the dials, that byte-for-byte identity at 0 must hold. The dials are orthogonal (one changes balance, the other firmness); stiffness runs first so order only matters at the clamps.

## The parity harness (legacy → snapshot → C#)

The differential parity gate is what guarantees the C# port matches the JS oracle, and it is fully reproducible from source:

1. **`legacy/parity-export.js`** builds a deterministic grid — the same 27-config `DT × PT × EL` grid as `legacy/sweep.js`, plus the 3 canonical fixture cars, across all six goals, swept over the two dials (each dial over `{-5, -2.5, 0, 2.5, 5}` with the other at 0, plus the four extreme combos). For each `(input, goal, dials)` case it runs the legacy `compute()` and records the result as `expected`.
2. It writes **`parity/cases.json`** (2340 cases). This file is **git-ignored** — it is regenerated, not committed.
3. **`ParityTests`** (with `ParityHarness`) loads `parity/cases.json`, replays each input through the Core `TuningEngine`, serializes the result to a JSON tree with the same camelCase/enum-token shape the JS emits, and asserts the snapshot is reproduced **exactly**. The gate is bidirectional and covers every numeric leaf (bit-for-bit after `-0` normalization), every boolean leaf (`singleSpeed`/`applicable`/`isEV`/over- & understeer-prone/`canTuneSusp` — these define tune *shape*), and every `summary[].k`/`summary[].v` chip string. Only `why.text`/`why.formula` wording is a **separate soft category** so prose drift can never mask a math/shape regression.

**Regeneration is automatic.** `Fh6Tuning.Tests.csproj` has a `GenerateParityCases` MSBuild target that runs `node legacy/parity-export.js` before build when `parity/cases.json` is missing or older than `legacy/tuning.js` or `legacy/parity-export.js`. If Node is unavailable and the snapshot is missing/stale, the build **fails loudly** rather than testing against an absent or stale baseline. **Node.js must be on PATH** to (re)generate the snapshot (incl. on CI). Keep the target's paths forward-slashed — a `\` is a literal filename char on the Linux runner.

The `springs._fFront`/`_fRear` JS values are documented internal scratch (target ride frequencies used only for the damping handoff and the why string); the exporter strips them and the C# `Springs` record omits them, so they are deliberately out of the tune contract.

## Build / test / run / publish

All commands run from the repo root.

```bash
dotnet build Fh6Tuning.sln              # build all projects (Core, Web, both test projects)
dotnet test  Fh6Tuning.sln              # run xUnit: parity gate, sweep, unit, web-service tests
dotnet test  Fh6Tuning.Tests            # engine + storage + parity only
dotnet run   --project Fh6Tuning.Web    # dev server (http://localhost:5221 / https://localhost:7083)
```

Running the tests (or building `Fh6Tuning.Tests`) regenerates `parity/cases.json` from `legacy/`, so **Node.js must be installed** for the parity gate. `dotnet run` does **not** hot-reload — **restart the server after editing `.razor`/`.css`** (or use `dotnet watch` for HMR). The `.claude/launch.json` `web` config (port 5221) is what the preview/verify tooling launches.

**Publish for GitHub Pages** — publish the Web project and deploy the WASM app's `wwwroot`:

```bash
dotnet publish Fh6Tuning.Web -c Release -o publish
# deploy the static bundle at:  publish/wwwroot
```

`publish/wwwroot` is the complete static site (`index.html`, `_framework/` runtime + DLLs, MudBlazor assets). Notes for Pages:
- If hosting under a project sub-path (`/fh6-tuning-calculator/`), set the `<base href>` accordingly (publish with `--base-href /fh6-tuning-calculator/` or edit `wwwroot/index.html`, which currently uses `<base href="/" />`).
- Provide a SPA 404 fallback (copy `index.html` to `404.html`) so deep links resolve.
- A `.nojekyll` file is only needed for a *branch*-based deploy (to stop Jekyll stripping `_framework`). The GitHub Actions artifact path used here never runs Jekyll, so none is created.

All of the above are automated by **`.github/workflows/deploy-pages.yml`** (push to `main`: test → publish → rewrite `<base href>` → `404.html` → deploy via GitHub Actions). **The Pages source must be set to "GitHub Actions"** in Settings → Pages: the workflow's `configure-pages` *enables* Pages but does **not** flip an existing "Deploy from a branch" source — leaving it on a branch makes GitHub's auto `pages-build-deployment` race the workflow on every push.

## MudBlazor / UI gotchas (dark theme)

- **Disable a ripple per-component with `Ripple="false"`** — never a global `.mud-ripple { display:none }`. MudBlazor stamps `mud-ripple` onto *functional* elements (e.g. the `MudSwitch` thumb base), so the global rule hides them (it ate the compare-toggle thumb).
- **The header is a sticky in-flow bar, not MudBlazor's fixed `MudAppBar`.** It uses `Fixed="false"` + `position:sticky` + `height:auto !important` on both `.fh6-appbar` and its `.mud-toolbar`, and zeroes `.mud-main-content`'s padding-top. MudBlazor's fixed `MudAppBar` is a fixed-height bar with a static body offset that clips wrapped/multi-line brand content on mobile.
- **`MudSelect` puts your `Class` on its inner `.mud-input-control`, not its outer `.mud-select` wrapper (the actual flex item).** To size/shrink a select in a flex row, wrap it in a project-owned div (e.g. `.fh6-setup-list-wrap`) and target that — don't couple CSS to `.mud-select`. Field `margin-top` is already zeroed app-wide by `.fh6-inputs .mud-input-control { margin-top: 0 }`.
- **A `<fieldset>` defaults to `min-width: min-content`**, so long content (e.g. a long saved-setup name) grows it past a fixed-width panel and overflows the next column; set `min-width: 0` on `.fh6-inputs fieldset`.
- **The router has no `FocusOnNavigate`** (removed): focusing the `<h1>` on every (re)load only painted a `:focus-visible` ring on the title — no a11y value with no client-side navigation. Don't re-add it.

## Testing conventions

- `Fh6Tuning.Tests/Fixtures.cs` holds the canonical legal slider ranges and base input fixtures; `Helpers.cs` provides the shared range/shape assertions. New engine assertions should reuse these so range-checking stays centralized.
- `SweepTests` is the invariant safety net (xUnit equivalent of `legacy/sweep.js`): no NaN/non-finite output, every output in its legal range, gears strictly descending, ride height within the part range, all six goals distinct per car, drivetrains distinct, and dial-0 byte-for-byte neutrality. Run it after any formula change.
- `ParityTests` is the exact-value gate against the JS oracle (above). After any formula change, change `legacy/tuning.js` first, let the snapshot regenerate, then update the C# port until parity is green.

## Domain knowledge

The formulas are **community-canonical Forza tuning math** (consistent across FH4/FH5/FH6), not official. Full sourced derivations, per-goal modifier tables, and edge cases live in `research/` (see `research/INDEX.md` and the `spec-*.md` files). When changing a formula, update the corresponding `research/spec-*.md`, the legacy `legacy/tuning.js` (the parity oracle) **and** the C# port, plus the `Why.Formula` string the card displays. `README.md` has a high-level formula table; `legacy/AUDIT.md` records a prior review pass.

Key edge cases the engine handles (don't regress these): FWD/RWD/AWD diff routing and ARB/camber/brake-bias inversion; EV single-speed gearing (the lone "1st" ratio + final drive are only correct as a *pair*, and target-top-speed solves the limiter ~7% past target because FH6 EV motors lose power near redline); front/mid/rear engine weight bias; single-wing aero kits sized to the car's balance tendency; stock suspension locking alignment/ARB.
