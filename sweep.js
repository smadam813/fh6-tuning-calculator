/* Broad invariant sweep for the FH6 tuning engine — run with:  node sweep.js
 *
 * Complements the example-based unit suite (test/) by fuzzing the full input
 * space and asserting INVARIANTS rather than exact values:
 *   • no NaN / non-finite output anywhere
 *   • every numeric output inside its legal Forza slider range
 *   • multi-gear ratios strictly descending
 *   • handlingBias = 0 is byte-for-byte identical to the no-bias baseline
 *   • each car's six goals are mutually distinct
 *   • varying only the drivetrain produces distinct tunes
 * Exits non-zero if any check fails. */
const { compute, GOALS } = require("./tuning.js");

const DT = ["FWD", "RWD", "AWD"], PT = ["ICE", "EV", "Hybrid"], EL = ["Front", "Mid", "Rear"];
const PI = ["D", "C", "B", "A", "S1", "S2", "X"];
const TC = ["Stock", "Street", "Sport", "Race", "Rally", "Drag", "Offroad"];
const SUS = ["Stock", "Street", "Sport", "Race", "Drift", "Offroad"];
const WEIGHTS = [1800, 3300, 4600], POWERS = [90, 400, 1100], FWP = [42, 52, 62];

// legal slider ranges
const LIMITS = {
  "tires.front": [15, 55], "tires.rear": [15, 55], "gearing.final": [2, 7],
  "alignment.camberF": [-5, 0], "alignment.camberR": [-5, 0], "alignment.toeF": [-5, 5],
  "alignment.toeR": [-5, 5], "alignment.caster": [1, 7], "arb.front": [1, 65], "arb.rear": [1, 65],
  "springs.rideF": null, "springs.rideR": null, "damping.reboundF": [1, 20], "damping.reboundR": [1, 20],
  "damping.bumpF": [1, 20], "damping.bumpR": [1, 20], "braking.balance": [40, 65], "braking.pressure": [80, 130],
  "differential.accel": [0, 100], "differential.decel": [0, 100],
};
const get = (o, p) => p.split(".").reduce((a, k) => (a == null ? a : a[k]), o);

// ---- build a deterministic config grid (3×3×3 axes => 81 base cars) ----
const configs = [];
let n = 0;
for (const dt of DT) for (const pt of PT) for (const el of EL) {
  const idx = n++;
  configs.push({
    drivetrain: dt, engineLocation: el, powertrain: pt, piClass: PI[idx % 7],
    power: POWERS[idx % 3], torque: 200 + (idx % 5) * 90, weight: WEIGHTS[idx % 3], frontWeightPct: FWP[idx % 3],
    gears: [4, 6, 8][idx % 3], tireCompound: TC[idx % 7], suspensionType: SUS[idx % 6],
    hasFrontAero: idx % 2 === 0, hasRearAero: idx % 3 !== 0, aeroInstalled: true,
    rideHeightMinF: 3.5, rideHeightMaxF: 6.5, rideHeightMinR: 3.5, rideHeightMaxR: 6.5, springRateMinF: 200, springRateMaxF: 1100, springRateMinR: 200, springRateMaxR: 1100,
    aeroFront: [30, 165], aeroRear: [50, 300], redlineRpm: 7000, tireDiameter: 26, targetTopSpeed: null,
  });
}

const BIAS_STEPS = []; for (let b = -5; b <= 5.0001; b += 0.5) BIAS_STEPS.push(Math.round(b * 10) / 10);

let calls = 0, checks = 0, errors = 0, goalDistinctOK = 0, dtDistinctOK = 0;
const fail = (msg) => { errors++; if (errors <= 12) console.log("FAIL:", msg); };
const finite = (v) => v == null || (typeof v === "number" && isFinite(v));

function rangeCheck(t, ctx) {
  // every numeric leaf finite
  JSON.stringify(t, (k, v) => { if (typeof v === "number") { checks++; if (!isFinite(v)) fail(`NaN ${k} @ ${ctx}`); } return v; });
  for (const p in LIMITS) {
    const lim = LIMITS[p]; if (!lim) continue;
    const v = get(t, p); if (v == null) continue;
    checks++;
    if (v < lim[0] - 1e-9 || v > lim[1] + 1e-9) fail(`${p}=${v} out of [${lim}] @ ${ctx}`);
  }
  // ride height within part range
  for (const p of ["springs.rideF", "springs.rideR"]) { const v = get(t, p); checks++; if (v < 3.5 - 1e-9 || v > 6.5 + 1e-9) fail(`${p}=${v} out of part range @ ${ctx}`); }
  // gears strictly descending
  const r = t.gearing.ratios;
  if (!t.gearing.singleSpeed) for (let k = 1; k < r.length; k++) { checks++; if (r[k] >= r[k - 1]) fail(`gears not descending @ ${ctx}`); }
}

for (const cfg of configs) {
  // goal distinctness (bias 0)
  const perGoal = GOALS.map((g) => { calls++; return JSON.stringify(compute(cfg, g)); });
  if (new Set(perGoal).size === GOALS.length) goalDistinctOK++; else fail(`goals not all distinct for cfg ${cfg.drivetrain}/${cfg.powertrain}`);

  for (const g of GOALS) {
    // bias-0 == no-bias baseline (byte for byte)
    const baseline = JSON.stringify(compute(cfg, g)); calls++;
    const zero = JSON.stringify(compute(Object.assign({}, cfg, { handlingBias: 0 }), g)); calls++;
    checks++; if (baseline !== zero) fail(`bias0 != baseline @ ${cfg.drivetrain}/${g}`);
    // range/NaN across the full bias range
    for (const b of BIAS_STEPS) { const t = compute(Object.assign({}, cfg, { handlingBias: b }), g); calls++; rangeCheck(t, `${cfg.drivetrain}/${cfg.powertrain}/${g}/bias${b}`); }
  }
}

// drivetrain distinctness: hold everything else, vary only drivetrain
for (const cfg of configs) {
  const tunes = DT.map((dt) => { calls++; return JSON.stringify(compute(Object.assign({}, cfg, { drivetrain: dt }), "Circuit")); });
  if (new Set(tunes).size === DT.length) dtDistinctOK++; else fail(`drivetrain not distinct for cfg ${cfg.powertrain}/${cfg.engineLocation}`);
}

console.log("=== SWEEP RESULTS ===");
console.log("car configs:", configs.length);
console.log("compute() calls:", calls);
console.log("individual output checks:", checks);
console.log(`goal-distinct configs OK: ${goalDistinctOK}/${configs.length}`);
console.log(`drivetrain-distinct cars OK: ${dtDistinctOK}/${configs.length}`);
console.log("errors:", errors);
console.log(errors === 0 ? "ALL CHECKS PASSED ✓" : "SWEEP FAILED ✗");
process.exit(errors === 0 ? 0 : 1);
