# Codebase Structure

**Analysis Date:** 2026-06-13

## Directory Layout

```
fh6-tuning-calculator/
├── index.html          # Markup, inputs/outputs, script load order
├── styles.css          # Dark mobile-first responsive theme
├── tuning.js           # Engine: pure compute(input, goal) -> tune
├── setups.js           # Saved-setups storage: pure validate/merge/serialize
├── app.js              # UI controller: DOM binding, units, render, setups wiring
├── sweep.js            # Standalone invariant fuzz sweep (node sweep.js)
├── README.md           # Project overview + file map
├── AUDIT.md            # Audit notes
├── research/           # Sourced formula provenance the engine is built on
│   ├── INDEX.md
│   ├── findings.json
│   ├── critique.md
│   └── spec-*.md       # Per-category formula specs
└── test/               # node:test suites + fixtures
    ├── fixtures.js     # Sample cars + legal RANGES
    ├── helpers.js      # Shared range/NaN assertions
    ├── unit.test.js    # One test per output parameter, exact-value contract
    ├── edge.test.js
    ├── failure.test.js
    ├── integration.test.js
    └── setups.test.js
```

## Directory Purposes

**`research/`:**
- Purpose: Provenance for every engine formula
- Contains: `spec-*.md` per category (gearing, springs-damping, alignment-arb, aero-diff, tires-braking, handling-bias, overall-stiffness), plus `findings.json`, `critique.md`, `INDEX.md`
- Key files: `research/INDEX.md` (entry point), `research/findings.json`

**`test/`:**
- Purpose: `node:test` regression + invariant coverage
- Contains: fixtures, shared helpers, five test suites
- Key files: `test/fixtures.js` (cars + `RANGES`), `test/helpers.js` (`assertAllInRange`)

## Key File Locations

**Entry Points:**
- `index.html`: page; loads the three scripts in order (`index.html:260-262`)
- `app.js` `init` (line 708): bootstraps the UI on `DOMContentLoaded`

**Configuration:**
- None — no package.json, lockfile, linter, or bundler config. Pure static files.

**Core Logic:**
- `tuning.js`: the engine (`compute` at line 903; per-category functions throughout)
- `setups.js`: storage helpers (`parseDb`, `mergeDb`, `upsertSetup`)
- `app.js`: rendering + binding

**Testing:**
- `test/*.test.js`: run via `node --test`
- `sweep.js`: standalone invariant sweep, `node sweep.js`

## Naming Conventions

**Files:**
- Source modules: lowercase single word — `tuning.js`, `setups.js`, `app.js`, `sweep.js`
- Tests: `<area>.test.js` under `test/`
- Research specs: `spec-<topic>.md`
- Docs: UPPERCASE — `README.md`, `AUDIT.md`

**Directories:**
- lowercase single word — `research/`, `test/`

## Where to Add New Code

**New tuning category or formula change:**
- Engine: add/edit a category function in `tuning.js`, wire it into the `tune` object in `compute` (`tuning.js:906-924`)
- Render: add a card to the `CARDS` array and a row group in `compareRowDefs` in `app.js` (`app.js:190`, `app.js:313`)
- Provenance: add a `research/spec-*.md`
- Tests: lock an exact value in `test/unit.test.js` and a range in `test/fixtures.js` `RANGES`

**New input field:**
- Add markup in `index.html`, read it in `readInputs` (`app.js:84`), include in `validate` if required (`tuning.js:840`); add to `FIELD_DIM`/`UNIT_LABEL` if unit-bound (`app.js:15-27`)

**New saved-setup behavior:**
- Pure logic in `setups.js`; DOM/`localStorage` wiring in `app.js` `wireSetups` (`app.js:640`)

**Utilities:**
- Engine-only helpers: top of `tuning.js` (`clamp`, `r1`, `r5`, etc., lines 41-47)
- UI-only helpers: top of `app.js` (`nf`, `toImp`, `escapeHtml`)

## Special Directories

**`research/`:**
- Purpose: formula sources; reference only, not loaded at runtime
- Generated: No · Committed: Yes

**`test/`:**
- Purpose: test code; not shipped to the browser
- Generated: No · Committed: Yes

---

*Structure analysis: 2026-06-13*
