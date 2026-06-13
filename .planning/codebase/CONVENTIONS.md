# Coding Conventions

**Analysis Date:** 2026-06-13

## Naming Patterns

**Files:**
- Lowercase, single-word `.js` files at repo root: `tuning.js`, `app.js`, `setups.js`, `sweep.js`.
- Tests live under `test/` named `<area>.test.js`: `unit.test.js`, `integration.test.js`, `edge.test.js`, `failure.test.js`, `setups.test.js`.
- No directory nesting for source — flat root layout.

**Functions:**
- camelCase verbs: `derive`, `compute`, `validate`, `readInputs`, `overallTireDiameter`, `emptyDb`.
- Short pure math helpers use terse names: `clamp`, `r1`, `r2`, `rHalf`, `r5`, `rInt`, `rEven` (rounding helpers in `tuning.js:42-47`).
- DOM helper `$(id)` for `getElementById` in `app.js`.

**Variables:**
- camelCase for locals: `frontWeightPct`, `gripFactor`, `rearAxle`, `oversteerProne`.
- Boolean flags read as predicates: `understeerProne`, `oversteerProne`, `singleSpeed`, `applicable` (`tuning.js:64-68`).

**Constants:**
- UPPER_SNAKE_CASE module constants: `GOALS`, `GOAL_META`, `PI_INDEX`, `RANGES`, `STORAGE_KEY`, `SCHEMA` (`tuning.js:30`, `setups.js:11-12`).
- Object lookup maps used in place of switch statements (e.g. `PI_INDEX`, `gripFactor` map in `tuning.js:60`).

**Types:**
- Plain JS objects, no TypeScript. The `compute()` return is a structured object with named sections (`tires`, `gearing`, `alignment`, `arb`, `springs`, `damping`, `aero`, `braking`, `differential`).

## Code Style

**Formatting:**
- No formatter config present (no `.prettierrc`, no `eslint.config`). Style is hand-maintained and consistent.
- 2-space indentation.
- Double quotes for strings.
- Semicolons always terminate statements.
- Trailing commas in multiline object/array literals (`GOAL_META` in `tuning.js:31-38`).

**Linting:**
- No linter configured. `"use strict";` declared at the top of every module and IIFE.

## Import Organization

**Module pattern (dual-export):**
- Source files (`tuning.js`, `setups.js`) wrap logic in an IIFE and dual-export so the browser and `node:test` share one file:
  ```js
  (function () {
    "use strict";
    // ...
    const API = { GOALS, GOAL_META, compute, validate, overallTireDiameter };
    if (typeof window !== "undefined") window.TUNING = API;
    if (typeof module !== "undefined" && module.exports) module.exports = API;
  })();
  ```
  (`tuning.js:948-949`)
- `app.js` is browser-only and reads globals: `const { GOALS, GOAL_META, compute, validate } = window.TUNING;` and `window.SETUPS` (`app.js:8-9`).

**Requires (Node/test side):**
- CommonJS `require("node:assert/strict")`, `require("./fixtures.js")`, `require(path.join(__dirname, "..", "tuning.js"))`.
- Use the `node:` prefix for builtins (`node:assert/strict`, `node:test`, `node:path`).
- No npm dependencies — zero `package.json`, zero `node_modules`. Everything is stdlib + browser globals.

## Error Handling

**Validation-first contract:**
- `validate(input)` returns `{ valid: boolean, errors: string[] }` rather than throwing — the UI refuses to render when invalid (`tuning.js`, exercised in `test/failure.test.js`).
- `compute()` is defensive: it never throws and always clamps every output to the legal Forza slider range via `clamp(x, lo, hi)`. A bad value cannot crash the engine (asserted in `failure.test.js`).
- Input fractions are clamped at the boundary (`clamp(i.frontWeightPct / 100, 0.2, 0.8)` in `tuning.js`).

**UI side:**
- `window.confirm` / `window.prompt` guard destructive setup actions (`app.js:648`, `669`, `733`).
- Status messages cleared on a timer; error state toggled via a `.error` CSS class (`app.js:544`).

## Logging

**Framework:** None. No `console.log` in production paths. `sweep.js` exits non-zero on invariant failure rather than logging noise.

## Comments

**When to Comment:**
- Every module opens with a banner block comment (`/* ===== ... ===== */`) describing purpose, units, and key formulas (`tuning.js:1-26`).
- Inline comments explain *why*, especially the physics/handling rationale (e.g. understeer/oversteer flag reasoning in `tuning.js:64-67`).
- Formulas are documented in the file header as the source of truth (ride-frequency springs, gearing, ARB, damping).

**JSDoc/TSDoc:** Not used. Plain banner + inline comments only.

## Function Design

**Size:** Small, single-purpose helpers (`clamp`, `r1`...). Larger orchestrators (`derive`, `compute`) compose them.

**Parameters:** Single input object passed through (`compute(input, goal)`, `derive(i)`). Overrides via `Object.assign({...defaults}, overrides)` (`baseInput` in `test/fixtures.js`).

**Return Values:** Pure and deterministic — `compute` returns a structured object; `validate` returns `{valid, errors}`; `setups` functions return normalized objects or `null` on failure rather than throwing.

**Purity:** Core engine (`tuning.js`) and storage logic (`setups.js`) are pure with no DOM access, which is what makes them directly testable under `node:test`. DOM lives only in `app.js`.

## Module Design

**Exports:** A single `API` object per module, dual-exported to `window.*` and `module.exports`.

**Barrel Files:** None. Flat root; `test/fixtures.js` re-exports the engine plus canonical cars and `RANGES` so test files have one import source.

---

*Convention analysis: 2026-06-13*
