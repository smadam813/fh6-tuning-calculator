# Car Setups Save/Load/Backup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Save named Car Setups to localStorage, load them back, and back up / restore the whole collection as a JSON file (merge-by-name on import).

**Architecture:** A new pure-logic module `setups.js` (dual-exported like `tuning.js`: `window.SETUPS` in the browser, `module.exports` under Node) owns the database shape, validation, serialization, and merge semantics — fully unit-tested with `node:test`. `app.js` adds only thin DOM wiring: snapshot/apply of the Car Setup panel, a Saved Setups UI block, and export/import via Blob download + hidden file input. No dependencies, no build step.

**Tech Stack:** Vanilla JS (IIFE modules), localStorage, `node:test` + `node:assert/strict` (Node 22 installed; run with `node --test`).

**Spec:** `docs/superpowers/specs/2026-06-12-car-setups-save-load-design.md`

**Branch:** `car-setups` (already created and checked out).

---

## File Structure

- **Create `setups.js`** — pure storage logic: `STORAGE_KEY`, `SCHEMA`, `emptyDb`, `validateSetup`, `parseDb`, `serializeDb`, `upsertSetup`, `deleteSetup`, `mergeDb`. No DOM, no `Date.now()` (callers stamp `savedAt`).
- **Create `test/setups.test.js`** — node:test suite for `setups.js` (same style as `test/unit.test.js`).
- **Modify `index.html`** — "Saved Setups" fieldset at the top of the Car Setup panel; `<script src="setups.js">` before `app.js`.
- **Modify `styles.css`** — small additions reusing `.ghost-btn` / `.hint`.
- **Modify `app.js`** — storage helpers + status line, snapshot/apply, dropdown render, event wiring; exclude the setups controls from the global live-`refresh` listener.
- **Modify `README.md`** — document the feature, add `setups.js` to the file list.

Conventions to follow (read before starting):
- Repo commit style: imperative subject, no `feat:` prefixes (see `git log --oneline`).
- Test style: header comment block, `"use strict"`, `require("node:test")` — see `test/edge.test.js`.
- Every commit message ends with the line `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: `setups.js` skeleton — `emptyDb` + `validateSetup`

**Files:**
- Create: `setups.js`
- Create: `test/setups.test.js`

- [ ] **Step 1: Write the failing tests**

Create `test/setups.test.js`:

```js
/* =====================================================================
   SETUPS TESTS — the pure storage logic behind saved Car Setups:
   envelope/entry validation, serialize<->parse round-trips, name-keyed
   upsert/delete, and backup-merge semantics (imported wins by name).
   ===================================================================== */
"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");
const SETUPS = require(path.join(__dirname, "..", "setups.js"));

// a complete, valid setup entry; override any key via `over`
function entry(over) {
  return Object.assign({
    name: "Test car",
    savedAt: "2026-06-12T00:00:00.000Z",
    units: "imperial",
    goal: "Circuit",
    dials: { handlingBias: "0", overallStiffness: "0" },
    fields: { drivetrain: "RWD", power: "400", torque: "" },
  }, over);
}

/* ---------------- emptyDb ---------------- */
test("emptyDb returns a v1 envelope with no setups", () => {
  assert.deepEqual(SETUPS.emptyDb(), { schema: 1, setups: [] });
});

/* ---------------- validateSetup ---------------- */
test("validateSetup accepts a complete entry and trims the name", () => {
  const e = SETUPS.validateSetup(entry({ name: "  Test car  " }));
  assert.ok(e);
  assert.equal(e.name, "Test car");
  assert.equal(e.units, "imperial");
  assert.equal(e.goal, "Circuit");
  assert.deepEqual(e.fields, { drivetrain: "RWD", power: "400", torque: "" });
});

test("validateSetup rejects entries without a usable name or fields object", () => {
  assert.equal(SETUPS.validateSetup(null), null);
  assert.equal(SETUPS.validateSetup("nope"), null);
  assert.equal(SETUPS.validateSetup(entry({ name: "" })), null);
  assert.equal(SETUPS.validateSetup(entry({ name: "   " })), null);
  assert.equal(SETUPS.validateSetup(entry({ name: 7 })), null);
  assert.equal(SETUPS.validateSetup(entry({ fields: "nope" })), null);
  assert.equal(SETUPS.validateSetup(entry({ fields: ["nope"] })), null);
  assert.equal(SETUPS.validateSetup(entry({ fields: null })), null);
});

test("validateSetup defaults missing units/goal/dials/savedAt safely", () => {
  const e = SETUPS.validateSetup({ name: "Bare", fields: {} });
  assert.ok(e);
  assert.equal(e.units, "imperial");
  assert.equal(e.goal, "");
  assert.deepEqual(e.dials, {});
  assert.equal(e.savedAt, "");
  // a bogus units value falls back rather than poisoning setUnits later
  assert.equal(SETUPS.validateSetup(entry({ units: "stone" })).units, "imperial");
  assert.equal(SETUPS.validateSetup(entry({ units: "metric" })).units, "metric");
});

test("validateSetup preserves unknown extra keys (forward compatibility)", () => {
  const e = SETUPS.validateSetup(entry({ futureThing: { x: 1 } }));
  assert.deepEqual(e.futureThing, { x: 1 });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `node --test test/setups.test.js`
Expected: FAIL — `Cannot find module ... setups.js`

- [ ] **Step 3: Write the minimal implementation**

Create `setups.js`:

```js
/* =====================================================================
   FH6 Tuning Calculator — saved-setups storage logic (pure, no DOM)
   Validates, serializes and merges the saved-setup database the UI
   keeps in localStorage and round-trips through JSON backup files.
   Dual-exported like tuning.js so node:test can require it directly.
   ===================================================================== */
(function () {
  "use strict";

  const STORAGE_KEY = "fh6-tuning.setups.v1";
  const SCHEMA = 1;

  function emptyDb() {
    return { schema: SCHEMA, setups: [] };
  }

  // Normalize an object into a storable setup entry, or null if it can't be
  // one. Requires a non-empty (trimmed) string name and an object of raw
  // field strings; units/goal/dials/savedAt get safe defaults. Unknown extra
  // keys are kept so a backup written by a newer app version survives a
  // round-trip through this one.
  function validateSetup(obj) {
    if (!obj || typeof obj !== "object" || Array.isArray(obj)) return null;
    if (typeof obj.name !== "string" || obj.name.trim() === "") return null;
    if (!obj.fields || typeof obj.fields !== "object" || Array.isArray(obj.fields)) return null;
    const entry = Object.assign({}, obj);
    entry.name = obj.name.trim();
    entry.units = obj.units === "metric" ? "metric" : "imperial";
    entry.goal = typeof obj.goal === "string" ? obj.goal : "";
    entry.dials = obj.dials && typeof obj.dials === "object" && !Array.isArray(obj.dials) ? obj.dials : {};
    entry.savedAt = typeof obj.savedAt === "string" ? obj.savedAt : "";
    return entry;
  }

  const API = { STORAGE_KEY, SCHEMA, emptyDb, validateSetup };
  if (typeof window !== "undefined") window.SETUPS = API;
  if (typeof module !== "undefined" && module.exports) module.exports = API;
})();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `node --test test/setups.test.js`
Expected: PASS — 5 tests, 0 fail

- [ ] **Step 5: Commit**

```bash
git add setups.js test/setups.test.js
git commit -m "Add saved-setups storage module: db envelope and entry validation

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `parseDb` + `serializeDb`

**Files:**
- Modify: `setups.js` (add two functions + export them)
- Modify: `test/setups.test.js` (append tests)

- [ ] **Step 1: Write the failing tests**

Append to `test/setups.test.js`:

```js
/* ---------------- serializeDb / parseDb ---------------- */
test("serializeDb -> parseDb round-trips a db losslessly", () => {
  const db = { schema: 1, setups: [SETUPS.validateSetup(entry({}))] };
  const res = SETUPS.parseDb(SETUPS.serializeDb(db));
  assert.equal(res.ok, true);
  assert.equal(res.skipped, 0);
  assert.deepEqual(res.db, db);
});

test("parseDb rejects garbage JSON and wrong envelopes", () => {
  for (const bad of ["{nope", "42", '"str"', "[]", "null",
      '{"setups":[]}',                       // missing schema
      '{"schema":"1","setups":[]}',          // schema not a number
      '{"schema":0,"setups":[]}',            // schema below 1
      '{"schema":1}',                        // missing setups
      '{"schema":1,"setups":{}}']) {         // setups not an array
    const res = SETUPS.parseDb(bad);
    assert.equal(res.ok, false, `expected reject: ${bad}`);
    assert.equal(typeof res.error, "string");
  }
});

test("parseDb drops invalid entries and counts them, keeping the valid ones", () => {
  const raw = JSON.stringify({ schema: 1, setups: [entry({}), { name: "" }, null, entry({ name: "Second" })] });
  const res = SETUPS.parseDb(raw);
  assert.equal(res.ok, true);
  assert.equal(res.skipped, 2);
  assert.deepEqual(res.db.setups.map((s) => s.name), ["Test car", "Second"]);
});

test("parseDb tolerates a future schema number, reading entry-by-entry", () => {
  const raw = JSON.stringify({ schema: 2, setups: [entry({ newKey: "kept" })] });
  const res = SETUPS.parseDb(raw);
  assert.equal(res.ok, true);
  assert.equal(res.db.schema, 1, "normalized to the schema this app writes");
  assert.equal(res.db.setups[0].newKey, "kept");
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `node --test test/setups.test.js`
Expected: FAIL — the 4 new tests error with `SETUPS.parseDb is not a function` (the 5 from Task 1 still pass)

- [ ] **Step 3: Write the minimal implementation**

In `setups.js`, insert after `validateSetup` (before `const API = ...`):

```js
  // Parse a JSON string (localStorage value or backup file) into a db.
  // -> { ok:true, db, skipped } with invalid entries dropped & counted, or
  // -> { ok:false, error } when the envelope itself is unusable.
  // A schema NEWER than ours is still read entry-by-entry (best effort)
  // rather than rejected.
  function parseDb(jsonString) {
    let raw;
    try {
      raw = JSON.parse(jsonString);
    } catch (e) {
      return { ok: false, error: "not valid JSON" };
    }
    if (!raw || typeof raw !== "object" || Array.isArray(raw)) return { ok: false, error: "not a setups backup (expected an object)" };
    if (typeof raw.schema !== "number" || raw.schema < 1) return { ok: false, error: "not a setups backup (bad schema)" };
    if (!Array.isArray(raw.setups)) return { ok: false, error: "not a setups backup (missing setups list)" };
    const setups = [];
    let skipped = 0;
    for (const item of raw.setups) {
      const entry = validateSetup(item);
      if (entry) setups.push(entry);
      else skipped++;
    }
    return { ok: true, db: { schema: SCHEMA, setups }, skipped };
  }

  // Pretty-printed both in localStorage and in export files (identical
  // format by design; size is irrelevant at this scale).
  function serializeDb(db) {
    return JSON.stringify(db, null, 2);
  }
```

Update the export line:

```js
  const API = { STORAGE_KEY, SCHEMA, emptyDb, validateSetup, parseDb, serializeDb };
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `node --test test/setups.test.js`
Expected: PASS — 9 tests, 0 fail

- [ ] **Step 5: Commit**

```bash
git add setups.js test/setups.test.js
git commit -m "Add setups db parse/serialize with per-entry validation

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: `upsertSetup` + `deleteSetup`

**Files:**
- Modify: `setups.js`
- Modify: `test/setups.test.js`

- [ ] **Step 1: Write the failing tests**

Append to `test/setups.test.js`:

```js
/* ---------------- upsertSetup / deleteSetup ---------------- */
test("upsertSetup adds a new entry and replaces an existing one by trimmed name", () => {
  let db = SETUPS.emptyDb();
  db = SETUPS.upsertSetup(db, entry({}));
  db = SETUPS.upsertSetup(db, entry({ name: "Other" }));
  assert.equal(db.setups.length, 2);
  // same trimmed name -> replace, not duplicate
  db = SETUPS.upsertSetup(db, entry({ name: "  Test car ", fields: { power: "900" } }));
  assert.equal(db.setups.length, 2);
  assert.deepEqual(db.setups.find((s) => s.name === "Test car").fields, { power: "900" });
});

test("upsertSetup ignores an invalid entry (db returned unchanged)", () => {
  const db = SETUPS.upsertSetup(SETUPS.emptyDb(), { name: "", fields: {} });
  assert.deepEqual(db, SETUPS.emptyDb());
});

test("upsertSetup and deleteSetup do not mutate their input db", () => {
  const before = SETUPS.upsertSetup(SETUPS.emptyDb(), entry({}));
  const snapshot = JSON.parse(JSON.stringify(before));
  SETUPS.upsertSetup(before, entry({ name: "Another" }));
  SETUPS.deleteSetup(before, "Test car");
  assert.deepEqual(JSON.parse(JSON.stringify(before)), snapshot);
});

test("deleteSetup removes exactly the named entry (trimmed)", () => {
  let db = SETUPS.emptyDb();
  db = SETUPS.upsertSetup(db, entry({}));
  db = SETUPS.upsertSetup(db, entry({ name: "Keep me" }));
  db = SETUPS.deleteSetup(db, " Test car ");
  assert.deepEqual(db.setups.map((s) => s.name), ["Keep me"]);
  // deleting a name that isn't there is a no-op
  assert.equal(SETUPS.deleteSetup(db, "ghost").setups.length, 1);
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `node --test test/setups.test.js`
Expected: FAIL — 4 new tests error with `SETUPS.upsertSetup is not a function`

- [ ] **Step 3: Write the minimal implementation**

In `setups.js`, insert after `serializeDb` (before `const API = ...`):

```js
  // Pure: returns a new db with `setup` replacing any same-name entry.
  // An object that doesn't validate leaves the db unchanged.
  function upsertSetup(db, setup) {
    const entry = validateSetup(setup);
    if (!entry) return db;
    const setups = db.setups.filter((s) => s.name !== entry.name);
    setups.push(entry);
    return { schema: SCHEMA, setups };
  }

  // Pure: returns a new db without the named entry (exact, trimmed match).
  function deleteSetup(db, name) {
    const n = String(name).trim();
    return { schema: SCHEMA, setups: db.setups.filter((s) => s.name !== n) };
  }
```

Update the export line:

```js
  const API = { STORAGE_KEY, SCHEMA, emptyDb, validateSetup, parseDb, serializeDb, upsertSetup, deleteSetup };
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `node --test test/setups.test.js`
Expected: PASS — 13 tests, 0 fail

- [ ] **Step 5: Commit**

```bash
git add setups.js test/setups.test.js
git commit -m "Add name-keyed upsert/delete for saved setups

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: `mergeDb`

**Files:**
- Modify: `setups.js`
- Modify: `test/setups.test.js`

- [ ] **Step 1: Write the failing tests**

Append to `test/setups.test.js`:

```js
/* ---------------- mergeDb ---------------- */
test("mergeDb: imported wins by name, unrelated existing entries survive", () => {
  let existing = SETUPS.emptyDb();
  existing = SETUPS.upsertSetup(existing, entry({ name: "A", fields: { power: "100" } }));
  existing = SETUPS.upsertSetup(existing, entry({ name: "B", fields: { power: "200" } }));
  const imported = {
    schema: 1,
    setups: [
      SETUPS.validateSetup(entry({ name: "B", fields: { power: "999" }, savedAt: "2026-01-01T00:00:00.000Z" })),
      SETUPS.validateSetup(entry({ name: "C", fields: { power: "300" } })),
    ],
  };
  const { db, added, updated } = SETUPS.mergeDb(existing, imported);
  assert.equal(added, 1);
  assert.equal(updated, 1);
  assert.deepEqual([...db.setups.map((s) => s.name)].sort(), ["A", "B", "C"]);
  const b = db.setups.find((s) => s.name === "B");
  assert.equal(b.fields.power, "999", "imported entry replaced the existing one");
  assert.equal(b.savedAt, "2026-01-01T00:00:00.000Z", "imported entry keeps its own savedAt");
});

test("mergeDb with an empty import changes nothing", () => {
  const existing = SETUPS.upsertSetup(SETUPS.emptyDb(), entry({}));
  const { db, added, updated } = SETUPS.mergeDb(existing, SETUPS.emptyDb());
  assert.equal(added + updated, 0);
  assert.deepEqual(db, existing);
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `node --test test/setups.test.js`
Expected: FAIL — 2 new tests error with `SETUPS.mergeDb is not a function`

- [ ] **Step 3: Write the minimal implementation**

In `setups.js`, insert after `deleteSetup` (before `const API = ...`):

```js
  // Merge a restored backup into the existing db: imported wins by name,
  // existing setups absent from the import are preserved, imported entries
  // keep their own savedAt. Pure; returns { db, added, updated }.
  function mergeDb(existing, imported) {
    let added = 0, updated = 0;
    const byName = new Map(existing.setups.map((s) => [s.name, s]));
    for (const s of imported.setups) {
      if (byName.has(s.name)) updated++;
      else added++;
      byName.set(s.name, s);
    }
    return { db: { schema: SCHEMA, setups: [...byName.values()] }, added, updated };
  }
```

Update the export line:

```js
  const API = { STORAGE_KEY, SCHEMA, emptyDb, validateSetup, parseDb, serializeDb, upsertSetup, deleteSetup, mergeDb };
```

- [ ] **Step 4: Run the full suite to verify everything passes**

Run: `node --test`
Expected: PASS — all existing tests plus 15 setups tests, 0 fail

- [ ] **Step 5: Commit**

```bash
git add setups.js test/setups.test.js
git commit -m "Add merge-by-name for restoring setups backups

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Saved Setups UI block (HTML + CSS)

**Files:**
- Modify: `index.html` (two edits)
- Modify: `styles.css` (one addition)

No JS tests for static markup; browser verification happens in Task 9.

- [ ] **Step 1: Add the fieldset to `index.html`**

Find:

```html
  <section class="panel inputs" aria-label="Car inputs">
    <h2 class="panel-title">Car Setup</h2>

    <fieldset>
      <legend>Identity</legend>
```

Replace with:

```html
  <section class="panel inputs" aria-label="Car inputs">
    <h2 class="panel-title">Car Setup</h2>

    <fieldset class="setups" id="setupsBlock">
      <legend>Saved Setups</legend>
      <div class="setup-row">
        <input type="text" id="setupName" placeholder="Setup name — e.g. Hummer EV S1" aria-label="Setup name" />
        <button id="setupSave" type="button" class="ghost-btn">Save</button>
      </div>
      <div class="setup-row">
        <select id="setupList" aria-label="Saved setups"></select>
        <button id="setupLoad" type="button" class="ghost-btn">Load</button>
        <button id="setupDelete" type="button" class="ghost-btn">Delete</button>
      </div>
      <div class="setup-row">
        <button id="setupExport" type="button" class="ghost-btn">Export JSON</button>
        <button id="setupImport" type="button" class="ghost-btn">Import JSON</button>
        <input type="file" id="setupFile" accept=".json,application/json" hidden />
      </div>
      <p class="hint setup-status" id="setupStatus" role="status" aria-live="polite"></p>
    </fieldset>

    <fieldset>
      <legend>Identity</legend>
```

- [ ] **Step 2: Load `setups.js` before `app.js`**

Find:

```html
<script src="tuning.js"></script>
<script src="app.js"></script>
```

Replace with:

```html
<script src="tuning.js"></script>
<script src="setups.js"></script>
<script src="app.js"></script>
```

- [ ] **Step 3: Add the styles**

In `styles.css`, insert after the `.tire-dia-out` rule (end of the "drive-tire spec" block, ~line 105):

```css
/* ---------- Saved setups ---------- */
.setups .setup-row { display: flex; gap: 8px; margin-bottom: 8px; }
.setups .setup-row > input[type="text"], .setups .setup-row > select { flex: 1; min-width: 0; }
.ghost-btn:disabled { opacity: .45; cursor: default; }
.ghost-btn:disabled:hover { border-color: var(--line); color: var(--text); }
#setupDelete:hover:not(:disabled) { border-color: var(--danger); color: var(--danger); }
.setup-status { min-height: 1.2em; margin-top: 2px; }
.setup-status.error { color: var(--danger); }
```

- [ ] **Step 4: Sanity-check the suite still passes**

Run: `node --test`
Expected: PASS (nothing JS-side changed)

- [ ] **Step 5: Commit**

```bash
git add index.html styles.css
git commit -m "Add Saved Setups block to the Car Setup panel

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: `app.js` — storage helpers, status line, listener exclusion

**Files:**
- Modify: `app.js` (three edits)

- [ ] **Step 1: Reference the new module at the top**

Find (app.js line ~8):

```js
  const { GOALS, GOAL_META, compute, validate, overallTireDiameter } = window.TUNING;
```

Replace with:

```js
  const { GOALS, GOAL_META, compute, validate, overallTireDiameter } = window.TUNING;
  const SETUPS = window.SETUPS;
```

- [ ] **Step 2: Exclude the setups controls from the global live-refresh wiring**

In `init()`, find:

```js
    // live updates on every input
    document.querySelectorAll("input, select").forEach((el) => {
      el.addEventListener("input", refresh);
      el.addEventListener("change", refresh);
    });
```

Replace with:

```js
    // live updates on every input — except the setups controls, which manage
    // saved tunes rather than describing the car
    document.querySelectorAll("input, select").forEach((el) => {
      if (el.closest("#setupsBlock")) return;
      el.addEventListener("input", refresh);
      el.addEventListener("change", refresh);
    });
```

- [ ] **Step 3: Add the storage helpers + status line**

Insert a new section in `app.js` after the `escapeHtml` function (immediately before `function init()`):

```js
  /* ---------- saved setups: status + localStorage access ---------- */
  let setupStatusTimer = null;
  function setupStatus(msg, isError) {
    const el = $("setupStatus");
    el.textContent = msg;
    el.classList.toggle("error", !!isError);
    clearTimeout(setupStatusTimer);
    if (msg) setupStatusTimer = setTimeout(() => { el.textContent = ""; el.classList.remove("error"); }, 4000);
  }

  // localStorage can be unavailable (private mode) or unreadable — both
  // degrade to an empty db with a status note; the calculator keeps working.
  function loadSetupsDb() {
    let raw = null;
    try {
      raw = localStorage.getItem(SETUPS.STORAGE_KEY);
    } catch (e) {
      setupStatus("Browser storage unavailable — setups won't persist.", true);
      return SETUPS.emptyDb();
    }
    if (raw == null) return SETUPS.emptyDb();
    const res = SETUPS.parseDb(raw);
    if (!res.ok) {
      setupStatus("Stored setups were unreadable — starting fresh.", true);
      return SETUPS.emptyDb();
    }
    return res.db;
  }

  function saveSetupsDb(db) {
    try {
      localStorage.setItem(SETUPS.STORAGE_KEY, SETUPS.serializeDb(db));
      return true;
    } catch (e) {
      setupStatus("Couldn't write browser storage — setups not saved.", true);
      return false;
    }
  }
```

- [ ] **Step 4: Verify nothing broke**

Run: `node --test`
Expected: PASS (app.js isn't under node tests; this catches accidental setups.js edits)

- [ ] **Step 5: Commit**

```bash
git add app.js
git commit -m "Wire setups storage helpers and exclude setup controls from live refresh

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: `app.js` — snapshot/apply, dropdown render, save/load/delete

**Files:**
- Modify: `app.js` (two edits)

- [ ] **Step 1: Add snapshot/apply/render/wiring functions**

Insert in `app.js` directly after the `saveSetupsDb` function added in Task 6 (still before `function init()`):

```js
  // Snapshot every input/select in the Car Setup panel (by element id), plus
  // units, goal and both dials — the "full picture" a saved setup captures.
  function snapshotSetup(name) {
    const fields = {};
    document.querySelectorAll(".inputs input, .inputs select").forEach((el) => {
      if (!el.id || el.closest("#setupsBlock")) return;
      fields[el.id] = el.value;
    });
    return {
      name,
      savedAt: new Date().toISOString(),
      units,
      goal: currentGoal,
      dials: { handlingBias: $("handlingBias").value, overallStiffness: $("overallStiffness").value },
      fields,
    };
  }

  function applySetup(s) {
    // Units first: setUnits converts the stale on-screen values, which is fine
    // because every panel field is overwritten right after; it also fixes the
    // unit labels and the toggle's active state.
    setUnits(s.units === "metric" ? "metric" : "imperial");
    Object.keys(s.fields).forEach((id) => {
      const el = $(id);
      // only fields that still exist, and only inside the Car Setup panel
      if (el && el.closest(".inputs") && !el.closest("#setupsBlock")) el.value = String(s.fields[id]);
    });
    if (s.dials.handlingBias != null) $("handlingBias").value = s.dials.handlingBias;
    if (s.dials.overallStiffness != null) $("overallStiffness").value = s.dials.overallStiffness;
    if (GOALS.includes(s.goal)) currentGoal = s.goal;
    refresh();
  }

  // Options are built via DOM (not innerHTML) so names with quotes are safe.
  function renderSetupList(db, selectedName) {
    const sel = $("setupList");
    sel.innerHTML = "";
    const sorted = [...db.setups].sort((a, b) => String(b.savedAt).localeCompare(String(a.savedAt)));
    if (!sorted.length) {
      const o = document.createElement("option");
      o.value = ""; o.disabled = true; o.selected = true;
      o.textContent = "— no saved setups —";
      sel.appendChild(o);
    }
    for (const s of sorted) {
      const o = document.createElement("option");
      o.value = s.name; o.textContent = s.name;
      if (s.name === selectedName) o.selected = true;
      sel.appendChild(o);
    }
    const has = sorted.length > 0;
    $("setupLoad").disabled = !has;
    $("setupDelete").disabled = !has;
    $("setupExport").disabled = !has;
  }

  function wireSetups() {
    renderSetupList(loadSetupsDb(), null);

    $("setupSave").addEventListener("click", () => {
      const name = $("setupName").value.trim();
      if (!name) { setupStatus("Give the setup a name first.", true); return; }
      const db = loadSetupsDb();
      if (db.setups.some((s) => s.name === name) && !window.confirm(`Overwrite the saved setup “${name}”?`)) return;
      const next = SETUPS.upsertSetup(db, snapshotSetup(name));
      if (saveSetupsDb(next)) { renderSetupList(next, name); setupStatus(`Saved “${name}” ✓`); }
    });

    $("setupList").addEventListener("change", () => {
      if ($("setupList").value) $("setupName").value = $("setupList").value;
    });

    $("setupLoad").addEventListener("click", () => {
      const name = $("setupList").value;
      const s = loadSetupsDb().setups.find((x) => x.name === name);
      if (!s) { setupStatus("Pick a setup to load.", true); return; }
      applySetup(s);
      $("setupName").value = s.name;
      setupStatus(`Loaded “${s.name}” ✓`);
    });

    $("setupDelete").addEventListener("click", () => {
      const name = $("setupList").value;
      if (!name) { setupStatus("Pick a setup to delete.", true); return; }
      if (!window.confirm(`Delete the saved setup “${name}”?`)) return;
      const next = SETUPS.deleteSetup(loadSetupsDb(), name);
      if (saveSetupsDb(next)) { renderSetupList(next, null); setupStatus(`Deleted “${name}” ✓`); }
    });
  }
```

- [ ] **Step 2: Call `wireSetups()` from `init()`**

Find (end of `init()` — the anchor must include the `});` closing the `copyBtn` listener, because `setUnits()` also ends with a bare `refresh();` + `}` and would be ambiguous):

```js
    });
    refresh();
  }
```

Replace with:

```js
    });
    wireSetups();
    refresh();
  }
```

- [ ] **Step 3: Verify the suite still passes**

Run: `node --test`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add app.js
git commit -m "Add save/load/delete UI wiring for car setups

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: `app.js` — export / import

**Files:**
- Modify: `app.js` (one edit)

- [ ] **Step 1: Add the export/import listeners**

Inside `wireSetups()`, find the end of the delete handler:

```js
    $("setupDelete").addEventListener("click", () => {
      const name = $("setupList").value;
      if (!name) { setupStatus("Pick a setup to delete.", true); return; }
      if (!window.confirm(`Delete the saved setup “${name}”?`)) return;
      const next = SETUPS.deleteSetup(loadSetupsDb(), name);
      if (saveSetupsDb(next)) { renderSetupList(next, null); setupStatus(`Deleted “${name}” ✓`); }
    });
  }
```

Replace with:

```js
    $("setupDelete").addEventListener("click", () => {
      const name = $("setupList").value;
      if (!name) { setupStatus("Pick a setup to delete.", true); return; }
      if (!window.confirm(`Delete the saved setup “${name}”?`)) return;
      const next = SETUPS.deleteSetup(loadSetupsDb(), name);
      if (saveSetupsDb(next)) { renderSetupList(next, null); setupStatus(`Deleted “${name}” ✓`); }
    });

    $("setupExport").addEventListener("click", () => {
      const db = loadSetupsDb();
      const blob = new Blob([SETUPS.serializeDb(db)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      const d = new Date(), pad = (n) => String(n).padStart(2, "0");
      a.href = url;
      a.download = `fh6-setups-${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}.json`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
      setupStatus(`Exported ${db.setups.length} setup${db.setups.length === 1 ? "" : "s"} ✓`);
    });

    $("setupImport").addEventListener("click", () => $("setupFile").click());
    $("setupFile").addEventListener("change", () => {
      const file = $("setupFile").files[0];
      $("setupFile").value = ""; // so re-picking the same file fires change again
      if (!file) return;
      file.text().then((text) => {
        const res = SETUPS.parseDb(text);
        if (!res.ok) { setupStatus(`Import failed: ${res.error}.`, true); return; }
        const merged = SETUPS.mergeDb(loadSetupsDb(), res.db);
        if (!saveSetupsDb(merged.db)) return;
        renderSetupList(merged.db, null);
        const parts = [`${merged.added} added`, `${merged.updated} updated`];
        if (res.skipped) parts.push(`${res.skipped} skipped`);
        setupStatus(`Imported: ${parts.join(", ")} ✓`);
      }, () => setupStatus("Couldn't read that file.", true));
    });
  }
```

- [ ] **Step 2: Verify the suite still passes**

Run: `node --test`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add app.js
git commit -m "Add JSON backup export and merge-by-name import for setups

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: Browser verification (preview tools)

**Files:** none (verification only)

Use the preview_* tools (`preview_start` on the repo directory — it's a static site, any static server works; then `preview_snapshot` / `preview_click` / `preview_fill` / `preview_eval` / `preview_console_logs`). Verify each item; fix and re-check anything that fails before moving on:

- [ ] **No console errors on load**; the Saved Setups block renders at the top of the Car Setup panel with Load/Delete/Export disabled (empty db) and the dropdown showing "— no saved setups —".
- [ ] **Save flow:** fill power=400, weight=3300, front weight=52; set Handling Bias to +1.5; pick the Drift goal tab; type name "Test RWD" → Save. Status shows `Saved "Test RWD" ✓`; dropdown now lists it; Load/Delete/Export enabled.
- [ ] **Persistence:** reload the page (`preview_eval: window.location.reload()`). Dropdown still lists "Test RWD".
- [ ] **Load flow:** blank the power field and reset the bias dial, then Load "Test RWD" → power back to 400, bias back to +1.5 (slider AND its label), goal tab back on Drift, tune renders.
- [ ] **Units cross-load:** switch to Metric, then Load "Test RWD" (saved imperial) → app flips back to Imperial, values exactly as saved (no double conversion: weight shows 3300, not a converted number).
- [ ] **Overwrite confirm:** Save again with the same name → confirm dialog appears (accept it; in the preview, stub it via `preview_eval: window.confirm = () => true` first if needed).
- [ ] **Empty name:** clear the name box, click Save → error status "Give the setup a name first.", nothing saved.
- [ ] **Export:** click Export JSON → status `Exported 1 setup ✓` (download itself can't be inspected in the preview; the localStorage payload can: `preview_eval: localStorage.getItem("fh6-tuning.setups.v1")` returns the pretty JSON envelope).
- [ ] **Import (merge):** simulate via eval —

```js
// preview_eval: build a backup blob and run it through the import path's logic
const res = window.SETUPS.parseDb(JSON.stringify({ schema: 1, setups: [
  { name: "Imported car", units: "metric", goal: "Rally",
    dials: { handlingBias: "0", overallStiffness: "0" },
    fields: { power: "300", weight: "1500", frontWeight: "55" } },
  { name: "", fields: {} }   // invalid -> should be skipped
]}));
JSON.stringify(res)
```

  Expect `ok: true`, `skipped: 1`. Then exercise the real file path manually only if the preview supports file pickers; otherwise this plus the unit tests covers it.
- [ ] **Delete flow:** Delete "Test RWD" (confirm stubbed) → dropdown returns to "— no saved setups —", buttons disabled; localStorage shows an empty setups array.
- [ ] **Corrupt storage:** `preview_eval: localStorage.setItem("fh6-tuning.setups.v1", "{broken")` then reload → app still renders; first setups interaction shows "Stored setups were unreadable — starting fresh." (loadSetupsDb runs at init via renderSetupList, so the status appears on load).
- [ ] **Mobile width:** `preview_resize` to ~380px — the setup rows wrap acceptably (flex rows with `min-width: 0` inputs).

No commit (nothing changed unless fixes were needed; commit any fixes with their own message).

---

### Task 10: README + final check

**Files:**
- Modify: `README.md` (two edits)

- [ ] **Step 1: Add `setups.js` to the file list**

Find:

```
tuning.js       the engine — pure compute(input, goal) -> tune
app.js          UI: live binding, units, compare table, copy
```

Replace with:

```
tuning.js       the engine — pure compute(input, goal) -> tune
setups.js       saved setups — pure storage logic (validate/merge/serialize)
app.js          UI: live binding, units, compare table, copy, saved setups
```

- [ ] **Step 2: Document the feature**

Insert a new section after the "Tuning dials" section (before "## The formulas (and why)"):

```markdown
## Saved setups

Save the whole Car Setup panel — plus both dials and the selected goal — under a
name, right in your browser (localStorage; no account, no server). Load or delete
saved setups from the dropdown at the top of the panel. **Export JSON** downloads
the whole collection as a backup file; **Import JSON** restores one, merging by
name (the file wins on conflicts — everything else you've saved stays put).
Setups store the raw field text plus the units they were entered in, so blanks
stay blank and metric setups reload exactly as typed.
```

- [ ] **Step 3: Run the full suite one last time**

Run: `node --test`
Expected: PASS — every test green

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Document saved setups in the README

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Done criteria

- `node --test` fully green (existing suite + 15 new setups tests).
- All Task 9 browser checks pass.
- Branch `car-setups` contains the spec, this plan, and one commit per task, ready for a PR to `main` (user squash-merges).
