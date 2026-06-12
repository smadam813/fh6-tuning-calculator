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
