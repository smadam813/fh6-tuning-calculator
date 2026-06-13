<!-- refreshed: 2026-06-13 -->
# Architecture

**Analysis Date:** 2026-06-13

## System Overview

```text
┌─────────────────────────────────────────────────────────────┐
│                       Browser (no build)                     │
│                       `index.html`                           │
│   loads 3 global-IIFE scripts in order via <script> tags     │
├──────────────────┬──────────────────┬───────────────────────┤
│   Tuning engine  │  Setups storage  │   UI controller        │
│   `tuning.js`    │   `setups.js`    │     `app.js`           │
│  window.TUNING   │  window.SETUPS   │  (DOMContentLoaded)    │
└────────┬─────────┴────────┬─────────┴──────────┬────────────┘
         │                  │                     │
         ▼                  ▼                     ▼
┌──────────────────┐ ┌──────────────────┐ ┌─────────────────────┐
│ pure compute()   │ │ localStorage +   │ │ DOM read/render     │
│ deterministic    │ │ JSON backup file │ │ live <input> events │
│ imperial math    │ │ (pure helpers)   │ │ unit conversion     │
└──────────────────┘ └──────────────────┘ └─────────────────────┘
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| Tuning engine | Pure `compute(input, goal)` → structured tune object; all 9 tuning categories + 2 post-process dials | `tuning.js` |
| Setups storage | Pure validate/parse/serialize/upsert/delete/merge of the saved-setups DB | `setups.js` |
| UI controller | Read DOM inputs, unit conversion, run engine, render cards/compare table/copy/saved-setups wiring | `app.js` |
| Markup + inputs | Static form, output containers, script load order | `index.html` |
| Styling | Dark mobile-first responsive theme | `styles.css` |
| Invariant sweep | Fuzz the full input space, assert range/NaN/distinctness invariants | `sweep.js` |
| Provenance | Sourced formula research the engine is built on | `research/` |

## Pattern Overview

**Overall:** Layered, no-build vanilla JS SPA with a pure functional core and a thin DOM controller.

**Key Characteristics:**
- Three modules, each an IIFE exposing a dual export: `window.X` for the browser and `module.exports = X` for `node:test` (`tuning.js:948-949`, `setups.js`).
- The engine (`tuning.js`) is pure and deterministic — no DOM, no I/O, no globals beyond its export.
- All engine math is imperial; `app.js` converts metric in/out at the boundary (`app.js:14`, `M2I` table).
- No package.json, bundler, or framework. Open `index.html` from `file://` directly.

## Layers

**UI / Controller layer:**
- Purpose: Bind DOM, convert units, orchestrate engine calls, render output
- Location: `app.js`
- Contains: input readers (`readInputs`), renderers (`renderCards`, `renderCompare`, `renderChangesPanel`), unit toggle (`setUnits`), saved-setup wiring (`wireSetups`)
- Depends on: `window.TUNING`, `window.SETUPS`, the DOM
- Used by: invoked on `DOMContentLoaded` (`app.js:740`)

**Engine layer:**
- Purpose: Derive a complete tune from car stats
- Location: `tuning.js`
- Contains: `derive`, per-category functions (`tires`, `gearing`, `alignment`, `arb`, `springs`, `damping`, `aero`, `braking`, `differential`), dial post-processors (`applyHandlingBias`, `applyOverallStiffness`), `validate`, `compute`, `overallTireDiameter`
- Depends on: nothing
- Used by: `app.js`, all `test/*.js`, `sweep.js`

**Storage layer:**
- Purpose: Persist/round-trip saved setups
- Location: `setups.js`
- Contains: `emptyDb`, `validateSetup`, `parseDb`, `serializeDb`, `upsertSetup`, `deleteSetup`, `mergeDb`
- Depends on: nothing (the localStorage call lives in `app.js`, keeping this pure)
- Used by: `app.js` (`loadSetupsDb`/`saveSetupsDb` wrap the actual `localStorage` access)

## Data Flow

### Primary Request Path (live tune)

1. User edits any `<input>`/`<select>`; an `input`/`change` listener fires `refresh` (`app.js:712-716`)
2. `refresh` short-circuits to welcome/errors if incomplete or invalid (`app.js:477`, `app.js:482`)
3. `readInputs` collects DOM values, converting metric→imperial via `toImp` (`app.js:84`)
4. `validate(input)` rejects bad input (`tuning.js:840`)
5. `compute(input, currentGoal)` runs `derive` then each category function, then dials (`tuning.js:903-934`)
6. `renderCards` / `renderCompare` write HTML into `#output` / `#compareWrap`, converting imperial→display via `fromImp` (`app.js:223`, `app.js:374`)

### Dials diff flow

1. When either dial ≠ 0, `compute` is also run with both dials centered → `base` (`app.js:496-498`)
2. `renderCards(live, base)` highlights changed rows in place
3. `renderChangesPanel(live, base)` lists plain-language before→after effects (`app.js:283`)

### Saved-setup flow

1. `snapshotSetup` captures every Car Setup field + units/goal/dials (`app.js:584`)
2. `SETUPS.upsertSetup` returns a new DB; `saveSetupsDb` writes to `localStorage` (`app.js:649-650`)
3. Import/export round-trip through `SETUPS.serializeDb`/`parseDb` and a Blob download (`app.js:674-705`)

**State Management:**
- Module-level mutable state in `app.js`: `units`, `currentGoal`, `setupStatusTimer`, `lastLoadSkipped`. No state in the engine.

## Key Abstractions

**Tune object:**
- Purpose: The single structured result of `compute` — one key per tuning category plus `summary`/`derived`
- Examples: built in `tuning.js:906-924`
- Pattern: each category function returns `{ ...values, why: { text, formula } }`

**Goal:**
- Purpose: One of six tuning intents (`Circuit`, `Drag`, `Drift`, `OffRoad`, `Rally`, `Touge`)
- Examples: `GOALS`/`GOAL_META` (`tuning.js:28-37`)
- Pattern: every category function branches on `goal`

**Derived quantities (`d`):**
- Purpose: Per-call computed facts (axle weights, grip factor, understeer/oversteer flags) shared by all categories
- Examples: `derive` (`tuning.js:54`)

## Entry Points

**`init` (app.js:708):**
- Location: `app.js`
- Triggers: `DOMContentLoaded`
- Responsibilities: build goal tabs, wire all input listeners, unit toggle, dial resets, copy button, saved setups, initial `refresh`

**`compute` (tuning.js:903):**
- Location: `tuning.js`
- Triggers: every `refresh`, every test, every sweep iteration
- Responsibilities: produce a full tune

## Architectural Constraints

- **Threading:** Single-threaded browser main thread. No workers, no async beyond clipboard/file reads.
- **Global state:** Three `window.*` namespaces (`TUNING`, `SETUPS`) + module-locals in `app.js`. No shared mutable engine state.
- **Script load order is load-bearing:** `tuning.js` then `setups.js` then `app.js` (`index.html:260-262`); `app.js` reads `window.TUNING`/`window.SETUPS` at IIFE top.
- **Circular imports:** None — strict one-directional dependency (app → engine/storage).
- **No build step:** Must keep working from `file://`; clipboard falls back to `prompt` when blocked (`app.js:732`).

## Anti-Patterns

### Putting I/O in the engine or storage module

**What happens:** `tuning.js` and `setups.js` are pure; the actual `localStorage` access lives in `app.js` (`loadSetupsDb`/`saveSetupsDb`).
**Why it's wrong:** Purity is what lets `node:test` `require()` these modules directly and what keeps `sweep.js` deterministic.
**Do this instead:** Keep DOM/`localStorage`/`Blob` access in `app.js`; pass plain objects to engine/storage functions.

### Doing math in display units

**What happens:** Mixing metric values into engine calls.
**Why it's wrong:** The engine assumes imperial everywhere; metric leaks corrupt every formula.
**Do this instead:** Convert with `toImp`/`fromImp` at the `app.js` boundary only (`app.js:33-34`).

## Error Handling

**Strategy:** Validate-then-render in the UI; defensive pure functions in the core.

**Patterns:**
- `validate(input)` returns `{ valid, errors[] }`; `refresh` renders an error card instead of a tune (`app.js:436`).
- `localStorage` and file reads are wrapped in try/catch that degrade gracefully with a status note (`app.js:550-580`, `app.js:694`).
- `parseDb` returns `{ ok, error }` / `{ ok, db, skipped }` instead of throwing (`setups.js:43`).

## Cross-Cutting Concerns

**Logging:** None (no console logging in app paths).
**Validation:** `tuning.js` `validate` for car stats; `setups.js` `validateSetup`/`parseDb` for stored data; HTML `escapeHtml` for rendered names/text (`app.js:533`).
**Authentication:** Not applicable — client-only, no backend.

---

*Architecture analysis: 2026-06-13*
