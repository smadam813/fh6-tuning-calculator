/* =====================================================================
   Differential parity exporter.

   Builds a deterministic grid of inputs and, for each (input, goal, dials)
   case, runs the legacy JS engine's compute() and records the result as the
   `expected` snapshot. The C# ParityTests theory replays each input through
   the ported Core engine and asserts byte-for-byte numeric parity.

   Grid:
     • the SAME 27-config grid logic as sweep.js (DT × PT × EL = 27 base cars),
     • PLUS the 3 canonical cars from test/fixtures.js,
   each across all 6 GOALS, with dial settings:
     • handlingBias swept over {-5,-2.5,0,2.5,5} (overallStiffness 0),
     • overallStiffness swept over {-5,-2.5,0,2.5,5} (handlingBias 0),
     • the four extreme combos [[-5,-5],[-5,5],[5,-5],[5,5]].

   Output: parity/cases.json — an array of { id, input, goal, expected }.
   Run with:  node legacy/parity-export.js
   ===================================================================== */
"use strict";
const fs = require("node:fs");
const path = require("node:path");
const { compute, GOALS } = require(path.join(__dirname, "tuning.js"));
const {
  CAR_LIGHT_RWD,
  CAR_HEAVY_AWD_EV,
  CAR_MID_RWD_HIGHPI,
} = require(path.join(__dirname, "test", "fixtures.js"));

/* ---- the SAME 27-config grid as sweep.js (DT × PT × EL) ---- */
const DT = ["FWD", "RWD", "AWD"], PT = ["ICE", "EV", "Hybrid"], EL = ["Front", "Mid", "Rear"];
const PI = ["D", "C", "B", "A", "S1", "S2", "R", "X"];
const TC = ["Stock", "Street", "Sport", "Race", "Rally", "Drag", "Offroad"];
const SUS = ["Stock", "Street", "Sport", "Race", "Drift", "Offroad"];
const WEIGHTS = [1800, 3300, 4600], POWERS = [90, 400, 1100], FWP = [42, 52, 62];

const sweepConfigs = [];
let n = 0;
for (const dt of DT) for (const pt of PT) for (const el of EL) {
  const idx = n++;
  sweepConfigs.push({
    drivetrain: dt, engineLocation: el, powertrain: pt, piClass: PI[idx % 8],
    power: POWERS[idx % 3], torque: 200 + (idx % 5) * 90, weight: WEIGHTS[idx % 3], frontWeightPct: FWP[idx % 3],
    gears: [4, 6, 8][idx % 3], tireCompound: TC[idx % 7], suspensionType: SUS[idx % 6],
    hasFrontAero: idx % 2 === 0, hasRearAero: idx % 3 !== 0, aeroInstalled: true,
    rideHeightMinF: 3.5, rideHeightMaxF: 6.5, rideHeightMinR: 3.5, rideHeightMaxR: 6.5,
    springRateMinF: 200, springRateMaxF: 1100, springRateMinR: 200, springRateMaxR: 1100,
    aeroFront: [30, 165], aeroRear: [50, 300], redlineRpm: 7000, tireDiameter: 26, targetTopSpeed: null,
  });
}

/* ---- base cars: 27 sweep configs + 3 canonical fixtures ---- */
const baseCars = [];
sweepConfigs.forEach((cfg, i) => baseCars.push({ label: `sweep${i}`, input: cfg }));
baseCars.push({ label: "carLightRwd", input: CAR_LIGHT_RWD });
baseCars.push({ label: "carHeavyAwdEv", input: CAR_HEAVY_AWD_EV });
baseCars.push({ label: "carMidRwdHighPi", input: CAR_MID_RWD_HIGHPI });

/* ---- dial settings ---- */
const DIAL_STEPS = [-5, -2.5, 0, 2.5, 5];
const COMBOS = [[-5, -5], [-5, 5], [5, -5], [5, 5]];

// Per base car, enumerate (handlingBias, overallStiffness) pairs:
//   • sweep handlingBias over the 5 steps, overallStiffness 0
//   • sweep overallStiffness over the 5 steps, handlingBias 0
//   • the four extreme combos
// Deduped (the bias=0/stiff=0 case appears in both sweeps) so each pair is unique.
function dialPairs() {
  const seen = new Set();
  const pairs = [];
  const add = (b, s) => {
    const key = `${b}|${s}`;
    if (seen.has(key)) return;
    seen.add(key);
    pairs.push([b, s]);
  };
  for (const b of DIAL_STEPS) add(b, 0);
  for (const s of DIAL_STEPS) add(0, s);
  for (const [b, s] of COMBOS) add(b, s);
  return pairs;
}

const PAIRS = dialPairs();

// springs._fFront / springs._fRear are documented JS-INTERNAL SCRATCH (the target ride
// frequencies, used only for the damping handoff and the why string) — they leak into the
// serialized springs object but are NOT part of the tune contract, and the C# Springs record
// deliberately omits them (see Fh6Tuning.Core/Tune.cs). Strip them from the snapshot so the
// expected tree reflects the public tune shape the engines are contracted to match.
function stripInternalScratch(tune) {
  if (tune && tune.springs) {
    delete tune.springs._fFront;
    delete tune.springs._fRear;
  }
  return tune;
}

const cases = [];
for (const car of baseCars) {
  for (const goal of GOALS) {
    for (const [bias, stiff] of PAIRS) {
      const input = Object.assign({}, car.input, { handlingBias: bias, overallStiffness: stiff });
      const expected = stripInternalScratch(compute(input, goal));
      const id = `${car.label}|${goal}|hb${bias}|os${stiff}`;
      cases.push({ id, input, goal, expected });
    }
  }
}

const outDir = path.join(__dirname, "..", "parity");
fs.mkdirSync(outDir, { recursive: true });
const outFile = path.join(outDir, "cases.json");
// 2-space pretty-print so diffs are readable; the C# side ignores formatting.
fs.writeFileSync(outFile, JSON.stringify(cases, null, 2));

console.log(`Wrote ${cases.length} parity cases to ${outFile}`);
console.log(`  base cars: ${baseCars.length} (27 sweep + 3 canonical)`);
console.log(`  goals: ${GOALS.length}`);
console.log(`  dial pairs per (car,goal): ${PAIRS.length}`);
