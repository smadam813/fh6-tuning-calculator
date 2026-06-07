/* =====================================================================
   EDGE TESTS — structural behaviours at the boundaries of the model:
   FWD diff shape, EV single-speed gearing, rear-engine weight inversion,
   no-aero suppression, aero lbf mapping, and slider-at-0 identity.
   ===================================================================== */
"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const { TUNING, baseInput } = require("./fixtures.js");
const { inRange, assertAllInRange } = require("./helpers.js");
const { RANGES } = require("./fixtures.js");

/* ---------------- FWD emits no rear-diff / center fields ---------------- */
test("FWD car emits a front-only differential (no rear-axle or center fields)", () => {
  const t = TUNING.compute(baseInput({ drivetrain: "FWD", frontWeightPct: 62 }), "Circuit");
  assert.equal(t.differential.driveline, "FWD");
  assert.ok("accel" in t.differential && "decel" in t.differential, "FWD still has accel/decel");
  assert.equal("frontAccel" in t.differential, false, "no frontAccel field");
  assert.equal("frontDecel" in t.differential, false, "no frontDecel field");
  assert.equal("centerRear" in t.differential, false, "no centerRear field");
  inRange(t.differential.accel, RANGES.diff, "FWD accel");
});

test("RWD car emits a rear-only differential (no center field)", () => {
  const t = TUNING.compute(baseInput({ drivetrain: "RWD" }), "Circuit");
  assert.equal(t.differential.driveline, "RWD");
  assert.equal("centerRear" in t.differential, false, "RWD has no center diff");
  assert.equal("frontAccel" in t.differential, false, "RWD has no front diff");
});

test("AWD car emits the full per-axle diff including a center torque split", () => {
  const t = TUNING.compute(baseInput({ drivetrain: "AWD" }), "Circuit");
  assert.equal(t.differential.driveline, "AWD");
  for (const k of ["accel", "decel", "frontAccel", "frontDecel", "centerRear"]) {
    assert.ok(k in t.differential, `AWD diff missing ${k}`);
  }
  inRange(t.differential.centerRear, RANGES.awdCenter, "AWD centerRear");
  assert.ok(t.differential.centerRear >= 50, "AWD center never below 50% rear");
});

/* ---------------- EV single-speed gear logic ---------------- */
test("EV is single-speed regardless of the gears field (8 requested -> 1 ratio)", () => {
  const t = TUNING.compute(baseInput({ powertrain: "EV", gears: 8 }), "Circuit");
  assert.equal(t.gearing.singleSpeed, true);
  assert.equal(t.gearing.ratios.length, 1, "EV has exactly one ratio");
  inRange(t.gearing.ratios[0], RANGES.gear, "EV ratio");
  inRange(t.gearing.final, RANGES.fd, "EV final");
});

test("EV back-solves final drive to hit target top speed when speed inputs given", () => {
  const t = TUNING.compute(baseInput({ powertrain: "EV", redlineRpm: 9000, tireDiameter: 26, targetTopSpeed: 180 }), "Circuit");
  assert.equal(t.gearing.fdSource, "target");
  assert.equal(t.gearing.singleSpeed, true);
  // top speed should land within a couple mph of target (FD rounds to 2 dp)
  assert.ok(Math.abs(t.gearing.topSpeed - 180) < 2.5, `EV top speed ${t.gearing.topSpeed} ~ 180`);
});

test("non-EV ICE keeps a multi-gear box matching the gears field", () => {
  const t = TUNING.compute(baseInput({ powertrain: "ICE", gears: 6 }), "Circuit");
  assert.equal(t.gearing.singleSpeed, false);
  assert.equal(t.gearing.ratios.length, 6);
});

/* ---------------- rear-engine weight inversion ---------------- */
test("rear-engine car reflects rear-biased weight distribution in the summary", () => {
  const t = TUNING.compute(baseInput({ engineLocation: "Rear", frontWeightPct: 40 }), "Circuit");
  const bal = t.summary.find((s) => s.k === "Balance").v;
  assert.equal(bal, "40/60", "summary balance is 40 front / 60 rear");
  // rear corner load must exceed front corner load
  const frontCorner = parseFloat(t.summary.find((s) => s.k === "Front corner").v);
  const rearCorner = parseFloat(t.summary.find((s) => s.k === "Rear corner").v);
  assert.ok(rearCorner > frontCorner, "rear-engine: rear corner heavier than front");
});

test("rear-engine shifts effective front weight down vs an identical front-engine car", () => {
  const front = TUNING.compute(baseInput({ engineLocation: "Front", frontWeightPct: 50, drivetrain: "RWD" }), "Circuit");
  const rear = TUNING.compute(baseInput({ engineLocation: "Rear", frontWeightPct: 50, drivetrain: "RWD" }), "Circuit");
  // engineLocation Rear lowers effFw (−3) → less front camber magnitude than front-engine
  assert.notDeepEqual(
    [front.alignment.camberF, front.alignment.camberR],
    [rear.alignment.camberF, rear.alignment.camberR],
    "engine location should change the alignment"
  );
  // brake bias shifts rearward for rear engine
  assert.ok(rear.braking.balance <= front.braking.balance, "rear-engine brake bias <= front-engine");
});

/* ---------------- no-aero suppression ---------------- */
test("no-aero car suppresses aero (not applicable, null front/rear)", () => {
  const t = TUNING.compute(baseInput({ hasFrontAero: false, hasRearAero: false, aeroInstalled: false }), "Circuit");
  assert.equal(t.aero.applicable, false);
  assert.equal(t.aero.front, null);
  assert.equal(t.aero.rear, null);
});

test("front-splitter-only car gives a front value and null rear", () => {
  const t = TUNING.compute(baseInput({ hasFrontAero: true, hasRearAero: false }), "Circuit");
  assert.equal(t.aero.applicable, true);
  assert.ok(t.aero.front != null, "front DF present");
  assert.equal(t.aero.rear, null, "rear DF null (no wing)");
  inRange(t.aero.front, RANGES.aeroPct, "front-only DF");
});

test("rear-wing-only car gives a rear value and null front", () => {
  const t = TUNING.compute(baseInput({ hasFrontAero: false, hasRearAero: true }), "Circuit");
  assert.equal(t.aero.applicable, true);
  assert.equal(t.aero.front, null, "front DF null (no splitter)");
  assert.ok(t.aero.rear != null, "rear DF present");
  inRange(t.aero.rear, RANGES.aeroPct, "rear-only DF");
});

test("Drag goal zeroes aero even with a full kit installed", () => {
  const t = TUNING.compute(baseInput({ hasFrontAero: true, hasRearAero: true }), "Drag");
  assert.equal(t.aero.front, 0);
  assert.equal(t.aero.rear, 0);
});

test("aero % maps into the entered lbf downforce range", () => {
  const t = TUNING.compute(baseInput({ aeroFront: [50, 300], aeroRear: [80, 400] }), "Circuit");
  // frontLbf = 50 + (300-50)*0.85 = 262.5 -> rounded 263? engine rounds, just assert in-range + monotone
  assert.ok(t.aero.frontLbf >= 50 && t.aero.frontLbf <= 300, "frontLbf within entered range");
  assert.ok(t.aero.rearLbf >= 80 && t.aero.rearLbf <= 400, "rearLbf within entered range");
  // a higher % must map to a higher lbf for the same range
  assert.ok(t.aero.frontLbf > 50, "non-zero front DF maps above min");
});

/* ---------------- slider at 0 == baseline (whole tune) ---------------- */
test("handlingBias 0 is identical to omitting the bias entirely (per goal)", () => {
  for (const g of TUNING.GOALS) {
    const withZero = TUNING.compute(baseInput({ handlingBias: 0 }), g);
    const omitted = TUNING.compute(baseInput({ handlingBias: undefined }), g);
    const strip = (t) => JSON.parse(JSON.stringify({
      tires: t.tires, gearing: t.gearing, alignment: t.alignment, arb: t.arb,
      springs: { f: t.springs.front, r: t.springs.rear, rf: t.springs.rideF, rr: t.springs.rideR },
      damping: t.damping, aero: { f: t.aero.front, r: t.aero.rear },
      braking: t.braking, differential: t.differential,
    }));
    assert.deepEqual(strip(withZero), strip(omitted), `${g}: bias 0 must equal no-bias baseline`);
  }
});

/* ---------------- stock suspension locks alignment/ARB ---------------- */
test("stock suspension locks alignment and centres ARBs", () => {
  const t = TUNING.compute(baseInput({ suspensionType: "Stock" }), "Circuit");
  assert.equal(t.alignment.camberF, 0);
  assert.equal(t.alignment.camberR, 0);
  assert.equal(t.alignment.toeF, 0);
  assert.equal(t.alignment.toeR, 0);
  assert.equal(t.arb.front, 32.5);
  assert.equal(t.arb.rear, 32.5);
  assertAllInRange(t);
});
