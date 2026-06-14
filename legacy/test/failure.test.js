/* =====================================================================
   FAILURE TESTS — validate() is the input contract the UI relies on to
   refuse rendering a broken tune. These assert it FLAGS nonsense
   (missing required numerics, non-finite values, weight<=0, front weight
   <=0 or >=100, negative power, gears<1, inverted ranges) and ACCEPTS
   reasonable input. compute() is also checked to stay defensive (it never
   throws and always clamps), so a bad value can't crash the engine.
   ===================================================================== */
"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const { TUNING, baseInput } = require("./fixtures.js");
const { assertAllInRange } = require("./helpers.js");

const goodMin = { power: 400, weight: 3300, frontWeightPct: 52 };

function expectInvalid(input, matcher, label) {
  const res = TUNING.validate(input);
  assert.equal(res.valid, false, `${label}: expected invalid`);
  assert.ok(Array.isArray(res.errors) && res.errors.length > 0, `${label}: expected errors array`);
  if (matcher) assert.ok(res.errors.some((e) => matcher.test(e)), `${label}: no error matched ${matcher}, got ${JSON.stringify(res.errors)}`);
}

/* ---------------- valid baseline ---------------- */
test("validate accepts a complete, sane input", () => {
  const res = TUNING.validate(baseInput({}));
  assert.equal(res.valid, true);
  assert.deepEqual(res.errors, []);
});

test("validate accepts the bare minimum required numerics", () => {
  const res = TUNING.validate(goodMin);
  assert.equal(res.valid, true, JSON.stringify(res.errors));
});

/* ---------------- missing required inputs ---------------- */
test("missing weight is flagged", () => {
  expectInvalid({ power: 400, frontWeightPct: 52 }, /weight/i, "missing weight");
});
test("missing power is flagged", () => {
  expectInvalid({ weight: 3300, frontWeightPct: 52 }, /power/i, "missing power");
});
test("missing front weight % is flagged", () => {
  expectInvalid({ power: 400, weight: 3300 }, /front weight/i, "missing frontWeightPct");
});
test("empty object reports multiple errors", () => {
  const res = TUNING.validate({});
  assert.equal(res.valid, false);
  assert.ok(res.errors.length >= 3, "empty input should flag all three required fields");
});

/* ---------------- non-finite required numerics ---------------- */
test("NaN weight is flagged", () => {
  expectInvalid({ power: 400, weight: NaN, frontWeightPct: 52 }, /weight/i, "NaN weight");
});
test("Infinity power is flagged", () => {
  expectInvalid({ power: Infinity, weight: 3300, frontWeightPct: 52 }, /power/i, "Infinity power");
});
test("non-numeric string weight is flagged", () => {
  expectInvalid({ power: 400, weight: "heavy", frontWeightPct: 52 }, /weight/i, "string weight");
});

/* ---------------- out-of-range physical nonsense ---------------- */
test("negative weight is flagged", () => {
  expectInvalid({ power: 400, weight: -100, frontWeightPct: 52 }, /weight.*greater than 0/i, "negative weight");
});
test("zero weight is flagged", () => {
  expectInvalid({ power: 400, weight: 0, frontWeightPct: 52 }, /weight.*greater than 0/i, "zero weight");
});
test("front weight % >= 100 is flagged", () => {
  expectInvalid({ power: 400, weight: 3300, frontWeightPct: 120 }, /front weight.*less than 100/i, ">100 front");
  expectInvalid({ power: 400, weight: 3300, frontWeightPct: 100 }, /front weight.*less than 100/i, "=100 front");
});
test("front weight % <= 0 is flagged", () => {
  expectInvalid({ power: 400, weight: 3300, frontWeightPct: 0 }, /front weight.*greater than 0/i, "0 front");
  expectInvalid({ power: 400, weight: 3300, frontWeightPct: -5 }, /front weight.*greater than 0/i, "negative front");
});
test("negative power is flagged", () => {
  expectInvalid({ power: -10, weight: 3300, frontWeightPct: 52 }, /power.*negative/i, "negative power");
});
test("power exactly 0 is allowed (rolling chassis)", () => {
  const res = TUNING.validate({ power: 0, weight: 3300, frontWeightPct: 52 });
  assert.equal(res.valid, true, JSON.stringify(res.errors));
});
test("gears < 1 is flagged", () => {
  expectInvalid({ power: 400, weight: 3300, frontWeightPct: 52, gears: 0 }, /gears.*at least 1/i, "0 gears");
});
test("negative torque is flagged", () => {
  expectInvalid({ power: 400, weight: 3300, frontWeightPct: 52, torque: -50 }, /torque.*negative/i, "negative torque");
});
test("inverted spring range is flagged", () => {
  expectInvalid({ power: 400, weight: 3300, frontWeightPct: 52, springRateMin: 900, springRateMax: 150 }, /spring rate max/i, "inverted springs");
});
test("non-positive spring min is flagged", () => {
  expectInvalid({ power: 400, weight: 3300, frontWeightPct: 52, springRateMin: 0, springRateMax: 900 }, /spring rate min/i, "zero spring min");
});
test("inverted ride-height range is flagged", () => {
  expectInvalid({ power: 400, weight: 3300, frontWeightPct: 52, rideHeightMin: 7, rideHeightMax: 4 }, /ride height max/i, "inverted ride");
});

/* ---------------- multiple problems reported together ---------------- */
test("multiple invalid fields are all reported", () => {
  const res = TUNING.validate({ power: -5, weight: -1, frontWeightPct: 150 });
  assert.equal(res.valid, false);
  assert.ok(res.errors.length >= 3, `expected >=3 errors, got ${res.errors.length}`);
  assert.ok(res.errors.some((e) => /power/i.test(e)), "power error present");
  assert.ok(res.errors.some((e) => /weight.*greater than 0/i.test(e)), "weight error present");
  assert.ok(res.errors.some((e) => /front weight/i.test(e)), "front weight error present");
});

/* ---------------- compute() stays defensive (never throws, always clamps) ---------------- */
test("compute does not throw on out-of-range input and still clamps to legal ranges", () => {
  // even with nonsense, compute should not crash and outputs must be clamped.
  const nonsense = baseInput({ frontWeightPct: 150, weight: 99999, power: 5000 });
  let t;
  assert.doesNotThrow(() => { t = TUNING.compute(nonsense, "Circuit"); });
  assertAllInRange(t);
});

test("compute clamps an extreme low-weight / low-power car into legal ranges", () => {
  const tiny = baseInput({ weight: 200, power: 1, frontWeightPct: 10, piClass: "D" });
  let t;
  assert.doesNotThrow(() => { t = TUNING.compute(tiny, "OffRoad"); });
  assertAllInRange(t);
});

test("compute handles all goals on a validated car without throwing", () => {
  const car = baseInput({});
  for (const g of TUNING.GOALS) {
    assert.doesNotThrow(() => TUNING.compute(car, g), `compute threw for goal ${g}`);
  }
});
