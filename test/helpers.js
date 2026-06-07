/* =====================================================================
   Shared assertion helpers for the engine test suite.
   ===================================================================== */
"use strict";
const assert = require("node:assert/strict");
const { RANGES } = require("./fixtures.js");

// assert a numeric value is finite and within [lo, hi] inclusive
function inRange(value, [lo, hi], label) {
  assert.ok(typeof value === "number" && Number.isFinite(value),
    `${label}: expected a finite number, got ${value}`);
  assert.ok(value >= lo && value <= hi,
    `${label}: ${value} is outside legal range [${lo}, ${hi}]`);
}

// Verify EVERY numeric output of a tune lands inside its legal slider range.
// Used by unit tests so any future formula drift that escapes a clamp is caught.
function assertAllInRange(t) {
  inRange(t.tires.front, RANGES.tirePsi, "tires.front");
  inRange(t.tires.rear, RANGES.tirePsi, "tires.rear");

  inRange(t.gearing.final, RANGES.fd, "gearing.final");
  t.gearing.ratios.forEach((r, idx) => inRange(r, RANGES.gear, `gearing.ratios[${idx}]`));

  inRange(t.alignment.camberF, RANGES.camber, "alignment.camberF");
  inRange(t.alignment.camberR, RANGES.camber, "alignment.camberR");
  inRange(t.alignment.toeF, RANGES.toe, "alignment.toeF");
  inRange(t.alignment.toeR, RANGES.toe, "alignment.toeR");
  inRange(t.alignment.caster, RANGES.caster, "alignment.caster");

  inRange(t.arb.front, RANGES.arb, "arb.front");
  inRange(t.arb.rear, RANGES.arb, "arb.rear");

  // spring rate is clamped to the part's own min/max (defaults 150–900 in fixtures)
  inRange(t.damping.reboundF, RANGES.damping, "damping.reboundF");
  inRange(t.damping.reboundR, RANGES.damping, "damping.reboundR");
  inRange(t.damping.bumpF, RANGES.damping, "damping.bumpF");
  inRange(t.damping.bumpR, RANGES.damping, "damping.bumpR");

  if (t.aero.applicable) {
    if (t.aero.front != null) inRange(t.aero.front, RANGES.aeroPct, "aero.front");
    if (t.aero.rear != null) inRange(t.aero.rear, RANGES.aeroPct, "aero.rear");
  }

  inRange(t.braking.balance, RANGES.brakeBalance, "braking.balance");
  inRange(t.braking.pressure, RANGES.brakePressure, "braking.pressure");

  inRange(t.differential.accel, RANGES.diff, "differential.accel");
  inRange(t.differential.decel, RANGES.diff, "differential.decel");
  if (t.differential.driveline === "AWD") {
    inRange(t.differential.frontAccel, RANGES.diff, "differential.frontAccel");
    inRange(t.differential.frontDecel, RANGES.diff, "differential.frontDecel");
    inRange(t.differential.centerRear, RANGES.awdCenter, "differential.centerRear");
  }
}

// assert spring rate within the supplied part min/max
function assertSpringInPart(t, input) {
  inRange(t.springs.front, [input.springRateMin, input.springRateMax], "springs.front");
  inRange(t.springs.rear, [input.springRateMin, input.springRateMax], "springs.rear");
  inRange(t.springs.rideF, [input.rideHeightMin, input.rideHeightMax], "springs.rideF");
  inRange(t.springs.rideR, [input.rideHeightMin, input.rideHeightMax], "springs.rideR");
}

// every `why` field present must be {text, formula} strings
function assertWhyShape(t) {
  const sections = ["tires", "gearing", "alignment", "arb", "springs", "damping", "aero", "braking", "differential"];
  for (const s of sections) {
    const why = t[s].why;
    assert.ok(why && typeof why.text === "string" && why.text.length > 0, `${s}.why.text must be a non-empty string`);
    assert.ok(typeof why.formula === "string" && why.formula.length > 0, `${s}.why.formula must be a non-empty string`);
  }
}

module.exports = { inRange, assertAllInRange, assertSpringInPart, assertWhyShape };
