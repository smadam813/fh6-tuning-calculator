## What this is

A **zero-build, zero-dependency** single-page web app that converts a car's known stats into a complete Forza Horizon 6 tune across every tuning category, for six driving goals (Circuit · Drag · Drift · Off-Road · Rally · Touge). There is no `package.json`, no bundler, and no framework — plain ES5-style IIFE scripts loaded via `<script>` tags in `index.html`, runnable straight from `file://`.

## Architecture

Three layers, kept deliberately separate so the engine and storage are pure and unit-testable while all DOM/IO lives in `app.js`:

| File | Role | Exports |
|---|---|---|
| `tuning.js` | **The engine.** Pure `compute(input, goal) → tune`. Deterministic, no IO. | `window.TUNING` / `module.exports` = `{ GOALS, GOAL_META, compute, validate, overallTireDiameter }` |
| `setups.js` | **Storage logic.** Pure validate/merge/serialize for saved setups. No `localStorage` access of its own. | `window.SETUPS` / `module.exports` = `{ STORAGE_KEY, SCHEMA, emptyDb, validateSetup, parseDb, serializeDb, upsertSetup, deleteSetup, mergeDb }` |
| `app.js` | **UI controller.** Live input binding, unit conversion, compare table, copy, `localStorage` glue, "what the sliders changed" panel. The only file that touches the DOM. | none (IIFE) |

**Dual-context export pattern:** `tuning.js` and `setups.js` each end with `if (typeof window !== "undefined") window.X = API; if (typeof module !== "undefined" && module.exports) module.exports = API;`. This is what lets the browser load them via `<script>` *and* the Node tests `require()` them. Preserve this pattern when adding modules.

**Imperial is the engine's only unit system.** `tuning.js` does all math in lb / in / lb/in / hp / lb-ft. `app.js` converts metric input → imperial before calling `compute`, and imperial → metric for display (see `M2I` factors and `FIELD_DIM` in `app.js`). Tire width/aspect/rim are unit-independent in Forza and are deliberately kept *out* of `FIELD_DIM` so the metric toggle never rewrites them. Keep all unit logic in `app.js`; never introduce metric into `tuning.js`.

**`compute` flow** (`tuning.js`): `derive(input)` computes shared quantities (corner loads, class tier, etc.) once, then per-category pure functions (`tires`, `gearing`, `alignment`, `arb`, `springs`, `damping`, `aero`, `braking`, `differential`) build the tune. Each category returns its numbers plus a `why: { text, formula }` shown in the UI. Every numeric output is **clamped to its legal Forza slider range**.

**The two dials are post-processors with a hard neutrality contract.** After the baseline tune is built, `applyOverallStiffness` (firmness/magnitude) runs, then `applyHandlingBias` (front/rear balance). **At 0, each dial is skipped entirely, so `compute` returns the per-goal baseline byte-for-byte** — this is an invariant the sweep verifies (`bias0 != baseline`, `stiff0 != baseline`). If you touch the dials, that byte-for-byte identity at 0 must hold. The dials are orthogonal (one changes balance, the other firmness); stiffness runs first so order only matters at the clamps.

## Testing conventions

- `test/fixtures.js` holds the canonical `RANGES` (legal slider bounds) and base input fixtures; `test/helpers.js` provides `assertAllInRange`, `assertSpringInPart`, `assertWhyShape`. New engine assertions should reuse these so range-checking stays centralized.
- `sweep.js` is the invariant safety net (no NaN, every output in range, gears strictly descending, all six goals distinct per car, drivetrains distinct, dial-0 neutrality). Run it after any formula change.

## Domain knowledge

The formulas are **community-canonical Forza tuning math** (consistent across FH4/FH5/FH6), not official. Full sourced derivations, per-goal modifier tables, and edge cases live in `research/` (see `research/INDEX.md` and the `spec-*.md` files). When changing a formula, update the corresponding `research/spec-*.md` and the `why.formula` string the card displays. `README.md` has a high-level formula table; `AUDIT.md` records a prior review pass.

Key edge cases the engine handles (don't regress these): FWD/RWD/AWD diff routing and ARB/camber/brake-bias inversion; EV single-speed gearing (the lone "1st" ratio + final drive are only correct as a *pair*, and target-top-speed solves the limiter ~7% past target because FH6 EV motors lose power near redline); front/mid/rear engine weight bias; single-wing aero kits sized to the car's balance tendency; stock suspension locking alignment/ARB.
