/* =====================================================================
   INTEGRATION TESTS — one base car, all six goals, plus the handling-bias
   slider. Asserts that goal-specific parameters move in the expected
   DIRECTION (relative comparisons, not just exact values) and that the
   bias dial produces three distinct, correctly-ordered output sets with
   bias 0 == the pre-slider baseline.
   ===================================================================== */
"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const { TUNING, baseInput } = require("./fixtures.js");
const { assertAllInRange } = require("./helpers.js");

const BASE = baseInput({}); // RWD front-engine ICE, full aero, A class, bias 0
const G = {};
for (const g of TUNING.GOALS) G[g] = TUNING.compute(BASE, g);

/* ---------------- every goal produces a legal tune ---------------- */
test("all six goals yield a fully in-range tune", () => {
  for (const g of TUNING.GOALS) assertAllInRange(G[g]);
});

/* ---------------- goal-specific directional assertions ---------------- */
test("Circuit ARB is stiffer than Off-Road ARB (both ends)", () => {
  assert.ok(G.Circuit.arb.front > G.OffRoad.arb.front, "front ARB Circuit > OffRoad");
  assert.ok(G.Circuit.arb.rear > G.OffRoad.arb.rear, "rear ARB Circuit > OffRoad");
});

test("Off-Road rides higher than Circuit (ride height)", () => {
  assert.ok(G.OffRoad.springs.rideF > G.Circuit.springs.rideF, "OffRoad rideF taller");
  assert.ok(G.OffRoad.springs.rideR > G.Circuit.springs.rideR, "OffRoad rideR taller");
});

test("Off-Road / Rally springs are softer than Circuit springs", () => {
  assert.ok(G.OffRoad.springs.front < G.Circuit.springs.front, "OffRoad softer front");
  assert.ok(G.Rally.springs.front < G.Circuit.springs.front, "Rally softer front");
});

test("Circuit runs more downforce than Drag (which is zeroed)", () => {
  assert.equal(G.Drag.aero.front, 0);
  assert.equal(G.Drag.aero.rear, 0);
  assert.ok(G.Circuit.aero.front > G.Drag.aero.front, "Circuit front DF > Drag");
  assert.ok(G.Circuit.aero.rear > G.Drag.aero.rear, "Circuit rear DF > Drag");
});

test("Circuit downforce >= Touge downforce (more aero for grip)", () => {
  assert.ok(G.Circuit.aero.front >= G.Touge.aero.front, "Circuit front DF >= Touge");
  assert.ok(G.Circuit.aero.rear >= G.Touge.aero.rear, "Circuit rear DF >= Touge");
});

test("Drift uses a much stiffer rear ARB than front (provoke rotation)", () => {
  assert.ok(G.Drift.arb.rear > G.Drift.arb.front * 1.5, "Drift rear ARB >> front ARB");
});

test("Drift front camber is far more negative than Circuit", () => {
  assert.ok(G.Drift.alignment.camberF < G.Circuit.alignment.camberF - 0.5, "Drift camberF much more negative");
});

test("Drift brake balance is rear-biased (48%) vs Circuit", () => {
  assert.equal(G.Drift.braking.balance, 48);
  assert.ok(G.Drift.braking.balance < G.Circuit.braking.balance, "Drift more rearward than Circuit");
});

test("Drag differential accel-locks the rear harder than Circuit", () => {
  assert.ok(G.Drag.differential.accel > G.Circuit.differential.accel, "Drag higher accel lock");
});

test("Off-Road brake pressure is eased vs Circuit", () => {
  assert.ok(G.OffRoad.braking.pressure < G.Circuit.braking.pressure, "OffRoad softer braking");
});

test("Off-Road / Rally final drive is shorter (numerically higher) than Circuit", () => {
  assert.ok(G.OffRoad.gearing.final > G.Circuit.gearing.final, "OffRoad FD shorter");
  assert.ok(G.Rally.gearing.final > G.Circuit.gearing.final, "Rally FD shorter");
});

test("all six goals are mutually distinct tunes (not the same numbers)", () => {
  const sig = (t) => JSON.stringify([
    t.arb.front, t.arb.rear, t.springs.front, t.springs.rear,
    t.aero.front, t.aero.rear, t.braking.balance, t.differential.accel,
    t.gearing.final,
  ]);
  const seen = new Set(TUNING.GOALS.map((g) => sig(G[g])));
  assert.equal(seen.size, TUNING.GOALS.length, "each goal should be a distinct tune");
});

/* ---------------- handling-bias slider ---------------- */
const biasN5 = TUNING.compute(baseInput({ handlingBias: -5 }), "Circuit");
const bias0 = TUNING.compute(baseInput({ handlingBias: 0 }), "Circuit");
const biasP5 = TUNING.compute(baseInput({ handlingBias: 5 }), "Circuit");

test("bias 0 equals the pre-slider baseline byte-for-byte", () => {
  // BASE already has handlingBias 0; G.Circuit is the baseline. Compare deep.
  const strip = (t) => JSON.parse(JSON.stringify({
    tires: t.tires, gearing: t.gearing, alignment: t.alignment, arb: t.arb,
    springs: { front: t.springs.front, rear: t.springs.rear, rideF: t.springs.rideF, rideR: t.springs.rideR },
    damping: t.damping, aero: { front: t.aero.front, rear: t.aero.rear },
    braking: t.braking, differential: t.differential,
  }));
  assert.deepEqual(strip(bias0), strip(G.Circuit));
});

test("bias -5 / 0 / +5 are three DISTINCT output sets", () => {
  const sig = (t) => JSON.stringify([t.arb.front, t.arb.rear, t.springs.front, t.springs.rear, t.braking.balance, t.differential.accel, t.aero.front, t.aero.rear]);
  const s = new Set([sig(biasN5), sig(bias0), sig(biasP5)]);
  assert.equal(s.size, 3, "the three bias settings must differ");
});

test("bias is correctly ordered: oversteer (+) frees the rear, understeer (−) plants it", () => {
  // front ARB: + softens front → descending across −5, 0, +5
  assert.ok(biasN5.arb.front > bias0.arb.front, "−5 front ARB stiffest");
  assert.ok(bias0.arb.front > biasP5.arb.front, "+5 front ARB softest");
  // rear ARB: + stiffens rear → ascending
  assert.ok(biasN5.arb.rear < bias0.arb.rear, "−5 rear ARB softest");
  assert.ok(bias0.arb.rear < biasP5.arb.rear, "+5 rear ARB stiffest");
  // front spring: + softens front → descending
  assert.ok(biasN5.springs.front > bias0.springs.front && bias0.springs.front > biasP5.springs.front, "front spring descends");
  // brake balance: + shifts forward → ascending
  assert.ok(biasN5.braking.balance < bias0.braking.balance && bias0.braking.balance < biasP5.braking.balance, "brake balance ascends");
  // diff accel lock: + raises → ascending
  assert.ok(biasN5.differential.accel < bias0.differential.accel && bias0.differential.accel < biasP5.differential.accel, "diff accel ascends");
  // aero: + raises front / lowers rear
  assert.ok(biasN5.aero.front < bias0.aero.front && bias0.aero.front < biasP5.aero.front, "aero front ascends");
  assert.ok(biasN5.aero.rear > bias0.aero.rear && bias0.aero.rear > biasP5.aero.rear, "aero rear descends");
});

test("bias extremes stay inside legal ranges", () => {
  assertAllInRange(biasN5);
  assertAllInRange(biasP5);
});

test("bias never erases goal character (Drift stays Drift)", () => {
  const driftBias = TUNING.compute(baseInput({ handlingBias: 5 }), "Drift");
  // Drift's welded rear accel-lock and rear-biased brake are preserved by design
  assert.equal(driftBias.differential.accel, G.Drift.differential.accel, "Drift accel lock untouched by bias");
  assert.equal(driftBias.braking.balance, G.Drift.braking.balance, "Drift brake balance untouched by bias");
});

test("bias moves the FWD front differential lock (accel/decel) in order", () => {
  const fwd = (b) => TUNING.compute(baseInput({ drivetrain: "FWD", frontWeightPct: 60, handlingBias: b }), "Circuit");
  const n = fwd(-5), z = fwd(0), p = fwd(5);
  assert.equal(z.differential.driveline, "FWD");
  // + bias raises FWD accel lock, − lowers it (clamped to 0–95 / decel 5–100)
  assert.ok(n.differential.accel < z.differential.accel && z.differential.accel < p.differential.accel, "FWD accel ascends with bias");
  assert.ok(p.differential.accel <= 95, "FWD accel stays <= 95");
  assert.ok(n.differential.decel >= 5, "FWD decel stays >= 5");
  assertAllInRange(p); assertAllInRange(n);
});

test("bias moves the AWD center torque split (rear %) in order, clamped 50–90", () => {
  const awd = (b) => TUNING.compute(baseInput({ drivetrain: "AWD", handlingBias: b }), "Circuit");
  const n = awd(-5), z = awd(0), p = awd(5);
  assert.ok(n.differential.centerRear < z.differential.centerRear, "−5 sends torque forward");
  assert.ok(z.differential.centerRear < p.differential.centerRear, "+5 sends torque rearward");
  assert.ok(p.differential.centerRear <= 90 && n.differential.centerRear >= 50, "center clamped 50–90");
});

/* ---------------- optional gearing physics (back-solved final drive) ---------------- */
test("non-EV gearing back-solves final drive to a target top speed", () => {
  const t = TUNING.compute(baseInput({ redlineRpm: 7000, tireDiameter: 26, targetTopSpeed: 160 }), "Circuit");
  assert.equal(t.gearing.fdSource, "target");
  assert.equal(t.gearing.singleSpeed, false);
  assert.ok(Array.isArray(t.gearing.speeds) && t.gearing.speeds.length === BASE.gears, "per-gear speeds computed");
  assert.ok(Math.abs(t.gearing.topSpeed - 160) < 1.5, `top speed ${t.gearing.topSpeed} ~ 160`);
});

test("gearing with redline + tire diameter but no target uses the HP heuristic FD", () => {
  const t = TUNING.compute(baseInput({ redlineRpm: 7000, tireDiameter: 26 }), "Circuit");
  assert.equal(t.gearing.fdSource, "heuristic");
  assert.ok(Array.isArray(t.gearing.speeds), "speeds still computed from redline + tire");
  assert.ok(t.gearing.topSpeed > 0, "top speed positive");
});
