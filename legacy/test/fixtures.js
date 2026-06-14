/* =====================================================================
   Shared test fixtures — engine handle + canonical cars + helpers.
   The engine is pure imperial; these inputs mirror what app.js readInputs()
   produces (imperial, with optional fields either numbers or null).
   ===================================================================== */
"use strict";
const path = require("node:path");
const TUNING = require(path.join(__dirname, "..", "tuning.js"));

// A complete imperial input with every field present. Individual cars below
// override only what differs, so each car is a single coherent object.
function baseInput(overrides) {
  return Object.assign(
    {
      drivetrain: "RWD",
      engineLocation: "Front",
      powertrain: "ICE",
      piClass: "A",
      power: 400,
      torque: 370,
      weight: 3300,
      frontWeightPct: 52,
      gears: 6,
      tireCompound: "Sport",
      suspensionType: "Race",
      hasFrontAero: true,
      hasRearAero: true,
      aeroInstalled: true,
      rideHeightMinF: 4.5,
      rideHeightMaxF: 7.0,
      rideHeightMinR: 4.5,
      rideHeightMaxR: 7.0,
      springRateMinF: 150,
      springRateMaxF: 900,
      springRateMinR: 150,
      springRateMaxR: 900,
      aeroFront: [null, null],
      aeroRear: [null, null],
      redlineRpm: null,
      tireDiameter: null,
      targetTopSpeed: null,
      handlingBias: 0,
    },
    overrides || {}
  );
}

/* ---- Three distinct canonical cars (per the unit-test brief) ---- */

// 1) Lightweight RWD ICE — front-engine, no aero, mid power. Hot hatch / coupe.
const CAR_LIGHT_RWD = baseInput({
  drivetrain: "RWD",
  engineLocation: "Front",
  powertrain: "ICE",
  piClass: "B",
  power: 300,
  torque: 280,
  weight: 2600,
  frontWeightPct: 53,
  gears: 6,
  tireCompound: "Sport",
  suspensionType: "Race",
  hasFrontAero: false,
  hasRearAero: false,
  aeroInstalled: false,
});

// 2) Heavy AWD EV — single-speed, nose-heavy, big mass, full aero kit.
const CAR_HEAVY_AWD_EV = baseInput({
  drivetrain: "AWD",
  engineLocation: "Front",
  powertrain: "EV",
  piClass: "S1",
  power: 760,
  torque: 720,
  weight: 5200,
  frontWeightPct: 54,
  gears: 1,
  tireCompound: "Race",
  suspensionType: "Race",
  hasFrontAero: true,
  hasRearAero: true,
  aeroInstalled: true,
});

// 3) Mid-engine RWD high-PI — rear-biased, high power, full aero.
const CAR_MID_RWD_HIGHPI = baseInput({
  drivetrain: "RWD",
  engineLocation: "Mid",
  powertrain: "ICE",
  piClass: "S2",
  power: 720,
  torque: 560,
  weight: 3100,
  frontWeightPct: 43,
  gears: 7,
  tireCompound: "Race",
  suspensionType: "Race",
  hasFrontAero: true,
  hasRearAero: true,
  aeroInstalled: true,
});

/* ---- Legal slider ranges (clamp targets) for "within range" assertions ---- */
const RANGES = {
  tirePsi: [15, 55],
  fd: [2, 7],
  gear: [0.5, 5.5],
  camber: [-5, 0],
  toe: [-5, 5],
  caster: [1, 7],
  arb: [1, 65],
  damping: [1, 20],
  aeroPct: [0, 100],
  brakeBalance: [40, 65],
  brakePressure: [80, 130],
  diff: [0, 100],
  awdCenter: [50, 90],
};

module.exports = {
  TUNING,
  baseInput,
  CAR_LIGHT_RWD,
  CAR_HEAVY_AWD_EV,
  CAR_MID_RWD_HIGHPI,
  RANGES,
};
