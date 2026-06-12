# Car Setups: Save / Load / Backup — Design

**Date:** 2026-06-12
**Status:** Approved

## Goal

Let the user save named Car Setups in the browser, load them back, and back up /
restore the whole collection as a JSON file. Zero dependencies, works on GitHub
Pages and `file://`, matches the existing vanilla-JS architecture.

## What a setup captures

The full picture (user's choice):

- Every `input` / `select` in the **Car Setup panel** (identity, performance,
  installed parts, part ranges, gearing refinement), stored as **raw field
  strings exactly as typed** — lossless; blanks stay blanks on load, consistent
  with the app's blank-by-default philosophy.
- The **units mode** (`imperial` / `metric`) the values were entered in.
- Both **dials**: `handlingBias` and `overallStiffness`.
- The selected **goal tab** (e.g. `Circuit`).

View state that is *not* saved: compare mode.

## Data model

One localStorage key: `fh6-tuning.setups.v1`. Value (export file is identical,
pretty-printed with 2-space indent):

```json
{
  "schema": 1,
  "setups": [
    {
      "name": "Hummer EV — S1",
      "savedAt": "2026-06-12T18:00:00.000Z",
      "units": "imperial",
      "goal": "Circuit",
      "dials": { "handlingBias": "0", "overallStiffness": "1.5" },
      "fields": { "drivetrain": "RWD", "power": "830", "torque": "" }
    }
  ]
}
```

- `fields` is keyed by element id; it contains every input/select in the panel
  at save time (including blanks). Snapshot/apply iterate the panel's elements
  automatically, so future panel fields are saved without touching this code.
- Names are trimmed; matching (overwrite, delete, merge) is by exact
  case-sensitive trimmed name.
- `setups` ordering in storage is irrelevant; the UI sorts by `savedAt`
  descending (newest first).
- Unknown extra keys on entries are preserved (forward compatibility — a
  round-trip through an older app version must not strip newer data).

## Module: `setups.js` (pure logic, no DOM)

Dual-export like `tuning.js`: `window.SETUPS` in the browser,
`module.exports` under Node so `node:test` can require it.

- `STORAGE_KEY`, `SCHEMA`
- `emptyDb()` → `{ schema: 1, setups: [] }`
- `validateSetup(obj)` → normalized entry or `null` (requires non-empty trimmed
  string `name`, object `fields`; missing `units`/`goal`/`dials`/`savedAt`
  tolerated with safe defaults)
- `parseDb(jsonString)` → `{ ok: true, db, skipped }` on success (invalid
  entries dropped and counted) or `{ ok: false, error }` (garbage JSON, wrong
  envelope: `schema` not a number ≥ 1, `setups` not an array). Used for both
  localStorage reads and import files.
- `serializeDb(db)` → pretty JSON string
- `upsertSetup(db, setup)` → new db with the entry replacing any same-name one
- `deleteSetup(db, name)` → new db
- `mergeDb(existing, imported)` → `{ db, added, updated }` — imported wins by
  name; existing setups not in the import are preserved; imported entries keep
  their own `savedAt`

All functions are pure (no `Date.now()` inside; callers stamp `savedAt`).

## UI

A `Saved Setups` fieldset at the top of the Car Setup panel (above Identity):

- Row 1: name text input (`setupName`) + **Save** button.
- Row 2: dropdown of saved setups (`setupList`, newest first) + **Load** +
  **Delete** buttons.
- Row 3: **Export JSON** / **Import JSON** ghost buttons + hidden
  `<input type="file" accept=".json,application/json">`.
- A status line (`setupStatus`, `role="status"`, `aria-live="polite"`) for
  feedback: "Saved ✓", "Imported 3 setups ✓ (1 skipped)", error messages.

Behavior:

- **Save** with empty name → refuse with hint in the status line. Save over an
  existing name → `window.confirm` first.
- **Delete** → `window.confirm` first.
- Selecting a setup in the dropdown fills the name box.
- **Load** applies the selected setup (see Load order below).
- **Export** downloads `fh6-setups-YYYY-MM-DD.json` (Blob + anchor click).
- **Import** parses the chosen file, merges by name (imported wins), reports
  added/updated/skipped counts. A malformed file changes nothing and reports
  the error.

Wiring note: `init()` currently attaches `refresh` listeners to **all**
`input, select` elements on the page. The setups controls (name box, dropdown,
file input) must be excluded from that auto-wiring — they are not tune inputs.

## Load order (matters)

1. `setUnits(setup.units)` — reuses existing conversion/labels logic; the
   converted old values are immediately overwritten in step 2, so no harm.
2. Write every `fields` entry whose element id still exists.
3. Set dials (`handlingBias`, `overallStiffness`).
4. Set `currentGoal = setup.goal` if it's a known goal, else keep current.
5. `refresh()`.

## Error handling

- All `localStorage` reads/writes wrapped in try/catch — private-mode or quota
  failures show a status-line message; the calculator itself keeps working.
- A corrupt localStorage value behaves like an empty db (with a status note),
  never a crash.
- Import: envelope and per-entry validation as in `parseDb` above.

## Testing

New `test/setups.test.js` (node:test, same style as existing suite):

- `emptyDb` shape; serialize → parse round-trip.
- `parseDb` rejects garbage JSON / non-object / bad envelope.
- `parseDb` skips invalid entries and counts them; preserves unknown entry keys.
- Future-schema tolerance: `schema: 2` envelope still parsed entry-by-entry.
- `upsertSetup` replaces by trimmed exact name; `deleteSetup` removes.
- `mergeDb`: imported wins, counts correct, unrelated existing entries kept.

DOM wiring is verified manually in the browser preview (save → reload page →
load; export → import round-trip; metric/imperial cross-load).

## Out of scope

Cross-device sync, per-setup export, rename-in-place, undo, IndexedDB.
