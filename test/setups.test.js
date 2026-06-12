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
  assert.equal(SETUPS.validateSetup([]), null);
  assert.equal(SETUPS.validateSetup(entry({ fields: 7 })), null);
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

test("validateSetup neutralizes a smuggled __proto__ key from raw JSON", () => {
  const raw = JSON.parse('{"name":"x","fields":{},"__proto__":{"evil":true}}');
  const e = SETUPS.validateSetup(raw);
  assert.ok(e);
  assert.equal(e.evil, undefined, "prototype not replaced by smuggled object");
  assert.equal(Object.getPrototypeOf(e), Object.prototype);
});

test("parseDb handles non-string input without throwing", () => {
  for (const bad of [undefined, null, 42, {}]) {
    const res = SETUPS.parseDb(bad);
    assert.equal(res.ok, false, `expected reject: ${String(bad)}`);
  }
});

test("parseDb dedupes same-name entries (last wins) and counts the drops", () => {
  const raw = JSON.stringify({ schema: 1, setups: [
    entry({ name: "Dup", fields: { power: "100" } }),
    entry({ name: " Dup ", fields: { power: "999" } }),  // trims to the same name
    entry({ name: "Other" }),
  ]});
  const res = SETUPS.parseDb(raw);
  assert.equal(res.ok, true);
  assert.equal(res.skipped, 1);
  assert.deepEqual(res.db.setups.map((s) => s.name), ["Dup", "Other"]);
  assert.equal(res.db.setups.find((s) => s.name === "Dup").fields.power, "999", "last entry wins");
});

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

test("deleteSetup ignores non-string names (nothing deleted)", () => {
  let db = SETUPS.emptyDb();
  db = SETUPS.upsertSetup(db, entry({ name: "undefined" }));
  db = SETUPS.upsertSetup(db, entry({ name: "null" }));
  assert.equal(SETUPS.deleteSetup(db, undefined).setups.length, 2);
  assert.equal(SETUPS.deleteSetup(db, null).setups.length, 2);
  assert.equal(SETUPS.deleteSetup(db, "  ").setups.length, 2);
});

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
