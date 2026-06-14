/* =====================================================================
   UNIT TESTS — one test per output parameter, across three distinct cars.
   For each parameter we assert (a) the EXACT value the engine produces for
   known inputs (locked-in via Circuit-goal computations) and (b) that the
   value sits inside its legal slider min/max. Exact values were captured
   from the engine and are the regression contract — a change in any formula
   that moves these must be deliberate.

   Cars:
     L = CAR_LIGHT_RWD     (lightweight RWD ICE, front engine, no aero, B)
     E = CAR_HEAVY_AWD_EV  (heavy AWD EV, single-speed, full aero, S1)
     M = CAR_MID_RWD_HIGHPI(mid-engine RWD, high power, full aero, S2)
   ===================================================================== */
"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const { TUNING, baseInput, CAR_LIGHT_RWD, CAR_HEAVY_AWD_EV, CAR_MID_RWD_HIGHPI, RANGES } = require("./fixtures.js");
const { inRange, assertAllInRange, assertSpringInPart, assertWhyShape } = require("./helpers.js");

const L = TUNING.compute(CAR_LIGHT_RWD, "Circuit");
const E = TUNING.compute(CAR_HEAVY_AWD_EV, "Circuit");
const M = TUNING.compute(CAR_MID_RWD_HIGHPI, "Circuit");

/* ---------------- TIRES ---------------- */
test("tires.front — exact + in range", () => {
  assert.equal(L.tires.front, 29.5);
  assert.equal(E.tires.front, 33.5);
  assert.equal(M.tires.front, 32);
  for (const t of [L, E, M]) inRange(t.tires.front, RANGES.tirePsi, "tires.front");
});
test("tires.rear — exact + in range", () => {
  assert.equal(L.tires.rear, 29);
  assert.equal(E.tires.rear, 33);
  assert.equal(M.tires.rear, 31);
  for (const t of [L, E, M]) inRange(t.tires.rear, RANGES.tirePsi, "tires.rear");
});

/* ---------------- GEARING ---------------- */
test("gearing.final — exact + in range", () => {
  assert.equal(L.gearing.final, 4.34);
  assert.equal(E.gearing.final, 3.89);
  assert.equal(M.gearing.final, 3.61);
  for (const t of [L, E, M]) inRange(t.gearing.final, RANGES.fd, "gearing.final");
});
test("gearing.ratios — exact + each in range + strictly descending", () => {
  assert.deepEqual(L.gearing.ratios, [3.21, 2.05, 1.57, 1.3, 1.13, 1]);
  assert.deepEqual(E.gearing.ratios, [1.39]); // single-speed EV
  assert.deepEqual(M.gearing.ratios, [2.88, 1.83, 1.41, 1.17, 1.01, 0.9, 0.81]);
  for (const t of [L, E, M]) {
    t.gearing.ratios.forEach((r, idx) => inRange(r, RANGES.gear, `ratios[${idx}]`));
    for (let k = 1; k < t.gearing.ratios.length; k++) {
      assert.ok(t.gearing.ratios[k] < t.gearing.ratios[k - 1], "ratios must strictly descend");
    }
  }
});
test("gearing.singleSpeed — EV true, others false; ratios length matches gears", () => {
  assert.equal(L.gearing.singleSpeed, false);
  assert.equal(E.gearing.singleSpeed, true);
  assert.equal(M.gearing.singleSpeed, false);
  assert.equal(L.gearing.ratios.length, CAR_LIGHT_RWD.gears);
  assert.equal(E.gearing.ratios.length, 1);
  assert.equal(M.gearing.ratios.length, CAR_MID_RWD_HIGHPI.gears);
});

test("overallTireDiameter — FH6 spec (width/aspect/rim) → rolling Ø in inches", () => {
  // rim + 2 × (width × aspect/100) / 25.4
  assert.ok(Math.abs(TUNING.overallTireDiameter(315, 30, 17) - 24.4409) < 1e-3); // rear drive tire
  assert.ok(Math.abs(TUNING.overallTireDiameter(225, 45, 17) - 24.9724) < 1e-3); // front tire
  assert.ok(Math.abs(TUNING.overallTireDiameter(245, 40, 19) - 26.7165) < 1e-3); // ~26" reference
  // any blank/non-positive part → null (caller falls back to the HP heuristic)
  assert.equal(TUNING.overallTireDiameter(null, 30, 17), null);
  assert.equal(TUNING.overallTireDiameter(315, 0, 17), null);
  assert.equal(TUNING.overallTireDiameter(315, 30, ""), null);
});

/* ---------------- ALIGNMENT ---------------- */
test("alignment.camberF — exact + in range", () => {
  assert.equal(L.alignment.camberF, -2.1);
  assert.equal(E.alignment.camberF, -2.3);
  assert.equal(M.alignment.camberF, -2.3);
  for (const t of [L, E, M]) inRange(t.alignment.camberF, RANGES.camber, "camberF");
});
test("alignment.camberR — exact + in range", () => {
  assert.equal(L.alignment.camberR, -0.9);
  assert.equal(E.alignment.camberR, -1);
  assert.equal(M.alignment.camberR, -1.2);
  for (const t of [L, E, M]) inRange(t.alignment.camberR, RANGES.camber, "camberR");
});
test("alignment.toeF — exact + in range", () => {
  // `+ 0` normalises IEEE-754 negative zero (engine may emit -0 from clamp/round)
  assert.equal(L.alignment.toeF + 0, 0);
  assert.equal(E.alignment.toeF + 0, 0);
  assert.equal(M.alignment.toeF + 0, 0);
  for (const t of [L, E, M]) inRange(t.alignment.toeF, RANGES.toe, "toeF");
});
test("alignment.toeR — exact + in range", () => {
  assert.equal(L.alignment.toeR, 0.1);
  assert.equal(E.alignment.toeR, 0);
  assert.equal(M.alignment.toeR, 0.2);
  for (const t of [L, E, M]) inRange(t.alignment.toeR, RANGES.toe, "toeR");
});
test("alignment.caster — exact + in range", () => {
  assert.equal(L.alignment.caster, 5.2);
  assert.equal(E.alignment.caster, 7);
  assert.equal(M.alignment.caster, 6.4);
  for (const t of [L, E, M]) inRange(t.alignment.caster, RANGES.caster, "caster");
});

/* ---------------- ANTI-ROLL BARS ---------------- */
test("arb.front — exact + in range", () => {
  assert.equal(L.arb.front, 15.94);
  assert.equal(E.arb.front, 26.04);
  assert.equal(M.arb.front, 11.29); // M is oversteer-prone (mid-engine, 43% front): firmer front bar
  for (const t of [L, E, M]) inRange(t.arb.front, RANGES.arb, "arb.front");
});
test("arb.rear — exact + in range", () => {
  assert.equal(L.arb.rear, 12.94);
  assert.equal(E.arb.rear, 27.33);
  assert.equal(M.arb.rear, 13.4); // oversteer-prone: softer rear bar (was 16.75) to free the rear
  for (const t of [L, E, M]) inRange(t.arb.rear, RANGES.arb, "arb.rear");
});

/* ---------------- SPRINGS & RIDE HEIGHT ---------------- */
test("springs.front — exact + within part min/max", () => {
  assert.equal(L.springs.front, 379);
  assert.equal(E.springs.front, 900);
  assert.equal(M.springs.front, 676);
  inRange(L.springs.front, [CAR_LIGHT_RWD.springRateMinF, CAR_LIGHT_RWD.springRateMaxF], "L.springs.front");
  inRange(E.springs.front, [CAR_HEAVY_AWD_EV.springRateMinF, CAR_HEAVY_AWD_EV.springRateMaxF], "E.springs.front");
  inRange(M.springs.front, [CAR_MID_RWD_HIGHPI.springRateMinF, CAR_MID_RWD_HIGHPI.springRateMaxF], "M.springs.front");
});
test("springs.rear — exact + within part min/max", () => {
  assert.equal(L.springs.rear, 240);
  assert.equal(E.springs.rear, 900);
  assert.equal(M.springs.rear, 786);
  inRange(L.springs.rear, [CAR_LIGHT_RWD.springRateMinR, CAR_LIGHT_RWD.springRateMaxR], "L.springs.rear");
  inRange(E.springs.rear, [CAR_HEAVY_AWD_EV.springRateMinR, CAR_HEAVY_AWD_EV.springRateMaxR], "E.springs.rear");
  inRange(M.springs.rear, [CAR_MID_RWD_HIGHPI.springRateMinR, CAR_MID_RWD_HIGHPI.springRateMaxR], "M.springs.rear");
});
test("springs.rideF / rideR — exact + within part min/max", () => {
  assert.equal(L.springs.rideF, 4.5);
  assert.equal(L.springs.rideR, 4.5);
  assert.equal(E.springs.rideF, 4.6);
  assert.equal(E.springs.rideR, 4.8);
  assert.equal(M.springs.rideF, 4.5);
  assert.equal(M.springs.rideR, 4.6);
  assertSpringInPart(L, CAR_LIGHT_RWD);
  assertSpringInPart(E, CAR_HEAVY_AWD_EV);
  assertSpringInPart(M, CAR_MID_RWD_HIGHPI);
});

/* ---------------- DAMPING ---------------- */
test("damping.reboundF — exact + in range", () => {
  assert.equal(L.damping.reboundF, 9);
  assert.equal(E.damping.reboundF, 10.8);
  assert.equal(M.damping.reboundF, 9.1);
  for (const t of [L, E, M]) inRange(t.damping.reboundF, RANGES.damping, "reboundF");
});
test("damping.reboundR — exact + in range", () => {
  assert.equal(L.damping.reboundR, 8.7);
  assert.equal(E.damping.reboundR, 10.6);
  assert.equal(M.damping.reboundR, 9.5);
  for (const t of [L, E, M]) inRange(t.damping.reboundR, RANGES.damping, "reboundR");
});
test("damping.bumpF — exact + in range", () => {
  assert.equal(L.damping.bumpF, 5.4);
  assert.equal(E.damping.bumpF, 6.4);
  assert.equal(M.damping.bumpF, 5.5);
  for (const t of [L, E, M]) inRange(t.damping.bumpF, RANGES.damping, "bumpF");
});
test("damping.bumpR — exact + in range", () => {
  assert.equal(L.damping.bumpR, 5.3);
  assert.equal(E.damping.bumpR, 6.3);
  assert.equal(M.damping.bumpR, 6);
  for (const t of [L, E, M]) inRange(t.damping.bumpR, RANGES.damping, "bumpR");
});
test("damping.bump is 40–70% of rebound for grip car (ratio guard)", () => {
  // M is a non-bypass goal/drivetrain (Circuit RWD), so the ratio guard holds.
  for (const end of [["bumpF", "reboundF"], ["bumpR", "reboundR"]]) {
    const ratio = M.damping[end[0]] / M.damping[end[1]];
    assert.ok(ratio >= 0.4 - 1e-9 && ratio <= 0.7 + 1e-9, `${end[0]}/${end[1]} ratio ${ratio} out of 0.4–0.7`);
  }
});

/* ---------------- AERO ---------------- */
test("aero — no-aero car not applicable, full-aero cars give % in range", () => {
  // L has no aero kit
  assert.equal(L.aero.applicable, false);
  assert.equal(L.aero.front, null);
  assert.equal(L.aero.rear, null);
  // E (AWD circuit) forces full front / min rear
  assert.equal(E.aero.applicable, true);
  assert.equal(E.aero.front, 100);
  assert.equal(E.aero.rear, 15);
  // M full kit
  assert.equal(M.aero.applicable, true);
  assert.equal(M.aero.front, 95);
  assert.equal(M.aero.rear, 90); // oversteer-prone mid-engine no longer has rear wing trimmed (was 85)
  for (const t of [E, M]) {
    inRange(t.aero.front, RANGES.aeroPct, "aero.front");
    inRange(t.aero.rear, RANGES.aeroPct, "aero.rear");
  }
});

/* ---------------- BRAKING ---------------- */
test("braking.balance — exact + in range", () => {
  assert.equal(L.braking.balance, 54);
  assert.equal(E.braking.balance, 57);
  assert.equal(M.braking.balance, 48);
  for (const t of [L, E, M]) inRange(t.braking.balance, RANGES.brakeBalance, "braking.balance");
});
test("braking.pressure — exact + in range", () => {
  assert.equal(L.braking.pressure, 105);
  assert.equal(E.braking.pressure, 120);
  assert.equal(M.braking.pressure, 110);
  for (const t of [L, E, M]) inRange(t.braking.pressure, RANGES.brakePressure, "braking.pressure");
});

/* ---------------- DIFFERENTIAL ---------------- */
test("differential.accel/decel — exact + in range (RWD cars)", () => {
  assert.equal(L.differential.driveline, "RWD");
  assert.equal(L.differential.accel, 56);
  assert.equal(L.differential.decel, 20);
  assert.equal(M.differential.driveline, "RWD");
  assert.equal(M.differential.accel, 38);
  assert.equal(M.differential.decel, 20);
  for (const t of [L, M]) {
    inRange(t.differential.accel, RANGES.diff, "diff.accel");
    inRange(t.differential.decel, RANGES.diff, "diff.decel");
    assert.equal(t.differential.accel % 2, 0, "accel must be an even %");
  }
});
test("differential AWD — full per-axle set, exact + in range", () => {
  assert.equal(E.differential.driveline, "AWD");
  assert.equal(E.differential.accel, 72);
  assert.equal(E.differential.decel, 30);
  assert.equal(E.differential.frontAccel, 26);
  assert.equal(E.differential.frontDecel, 5);
  assert.equal(E.differential.centerRear, 83);
  inRange(E.differential.accel, RANGES.diff, "rear accel");
  inRange(E.differential.decel, RANGES.diff, "rear decel");
  inRange(E.differential.frontAccel, RANGES.diff, "front accel");
  inRange(E.differential.frontDecel, RANGES.diff, "front decel");
  inRange(E.differential.centerRear, RANGES.awdCenter, "center rear");
});

/* ---------------- OVERSTEER-PRONE COMPENSATION ---------------- */
// A rear-engine / rear-biased car must get a tune that fights oversteer across the
// ARB, the AWD centre/rear diff, AND aero — not the tail-happy default. Mirrors the
// real car that surfaced this: AWD rear-engine, 46% front, Street tires, full aero.
const OS = baseInput({
  drivetrain: "AWD", engineLocation: "Rear", powertrain: "ICE", piClass: "A",
  power: 450, torque: 369, weight: 2614, frontWeightPct: 46, gears: 8,
  tireCompound: "Street", aeroFront: [122, 203], aeroRear: [362, 702],
});
const OSt = TUNING.compute(OS, "Circuit");

test("oversteer-prone: rear-engine AWD is flagged; a balanced front-engine AWD is not", () => {
  assert.equal(OSt.derived.oversteerProne, true);
  const bal = TUNING.compute(baseInput({ drivetrain: "AWD", engineLocation: "Front", frontWeightPct: 50 }), "Circuit");
  assert.equal(bal.derived.oversteerProne, false);
  assert.equal(bal.aero.rear, 15); // unchanged: balanced AWD keeps the min-rear-wing default
});
test("oversteer-prone AWD: centre split + rear locks pulled back from the sharpened-RWD default", () => {
  assert.ok(OSt.differential.centerRear <= 66, `center ${OSt.differential.centerRear} should be <= 66`);
  assert.ok(OSt.differential.accel <= 55, `rear accel ${OSt.differential.accel} should be <= 55`);
  inRange(OSt.differential.centerRear, RANGES.awdCenter, "center");
});
test("oversteer-prone: roll stiffness shifted forward (rear ARB no longer stiffer than front)", () => {
  assert.ok(OSt.arb.rear <= OSt.arb.front + 0.5, `rear ARB ${OSt.arb.rear} must not exceed front ${OSt.arb.front}`);
});
test("oversteer-prone AWD: rear wing planted, aero balance rear-biased by actual lbf", () => {
  assert.ok(OSt.aero.rear >= 80, `rear wing ${OSt.aero.rear}% should be raised, not floored`);
  assert.ok(OSt.aero.rearLbf > OSt.aero.frontLbf, `rear DF ${OSt.aero.rearLbf} must exceed front ${OSt.aero.frontLbf}`);
});

/* ---------------- WHOLE-TUNE INVARIANTS ---------------- */
test("every numeric output is inside its legal range (all 3 cars)", () => {
  assertAllInRange(L);
  assertAllInRange(E);
  assertAllInRange(M);
});
test("every section carries a {text, formula} why (all 3 cars)", () => {
  assertWhyShape(L);
  assertWhyShape(E);
  assertWhyShape(M);
});
test("summary strip + derived populated", () => {
  for (const [t, car] of [[L, CAR_LIGHT_RWD], [E, CAR_HEAVY_AWD_EV], [M, CAR_MID_RWD_HIGHPI]]) {
    assert.ok(Array.isArray(t.summary) && t.summary.length === 5, "summary has 5 chips");
    assert.ok(t.derived && typeof t.derived.pw === "number", "derived.pw present");
    // power-to-weight chip matches the input
    const pwChip = t.summary.find((s) => s.k === "Power-to-weight");
    assert.ok(pwChip.v.startsWith((Math.round(car.power / car.weight * 100) / 100).toString()), "pw chip matches");
  }
});

/* ---------------- R CLASS (between S2 and X) ---------------- */
// R must be a first-class entry in the PI ladder, NOT silently fall back to the
// A-class default (the bug: R missing from the dropdown left piIdx → 3).
test("piClass R — resolves to its own ladder slot between S2 and X, classTier Race", () => {
  const t = TUNING.compute(baseInput({ piClass: "R" }), "Circuit");
  assert.equal(t.derived.piIdx, 6, "R sits at index 6 (between S2=5 and X=7)");
  assert.equal(t.derived.classTier, "Race");
  assertAllInRange(t); // R must produce a fully legal tune, not just a fallback
});
test("piClass R — per-class tune math (caster, ARB) interpolates between S2 and X", () => {
  const caster = (pi) => TUNING.compute(baseInput({ piClass: pi, weight: 3000 }), "Circuit").alignment.caster;
  const arb = (pi) => TUNING.compute(baseInput({ piClass: pi, weight: 3000 }), "Circuit").arb.front;
  assert.ok(caster("S2") <= caster("R") && caster("R") <= caster("X"),
    `caster S2(${caster("S2")}) ≤ R(${caster("R")}) ≤ X(${caster("X")})`);
  assert.ok(caster("R") > caster("A"), "R caster must exceed A (proves no A fallback)");
  // ARB stiffness % decreases with class; R sits between S2 and X → front bar between them
  assert.ok(arb("S2") >= arb("R") && arb("R") >= arb("X"),
    `arb.front S2(${arb("S2")}) ≥ R(${arb("R")}) ≥ X(${arb("X")})`);
});
