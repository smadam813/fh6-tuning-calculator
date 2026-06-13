# Testing Patterns

**Analysis Date:** 2026-06-13

## Test Framework

**Runner:**
- Node.js built-in test runner — `node:test`. No third-party framework, no `package.json`, no `node_modules`.
- Config: none required. Tests are discovered by the `node --test` runner under `test/`.

**Assertion Library:**
- Node built-in `node:assert/strict` (imported as `assert`).

**Run Commands:**
```bash
node --test                                  # Run all tests in test/
node --test test/unit.test.js                # Run a single file
node --test --experimental-test-coverage     # Run with coverage (per .gitignore note)
node sweep.js                                 # Run the broad invariant fuzz sweep (exits non-zero on failure)
```

## Test File Organization

**Location:**
- Separate `test/` directory (not co-located with source).

**Naming:**
- `<area>.test.js`: `unit.test.js`, `integration.test.js`, `edge.test.js`, `failure.test.js`, `setups.test.js`.
- Shared, non-test support files: `fixtures.js` (engine handle + canonical cars + `RANGES`), `helpers.js` (assertion helpers).

**Structure:**
```
test/
├── fixtures.js          # baseInput(), CAR_* cars, RANGES, re-exports TUNING
├── helpers.js           # inRange, assertAllInRange, assertSpringInPart, assertWhyShape
├── unit.test.js         # exact-value regression contract, per output parameter
├── integration.test.js  # all six goals + bias dial, directional assertions
├── edge.test.js         # boundary/structural behaviours
├── failure.test.js      # validate() contract + compute() defensiveness
└── setups.test.js       # saved-setups storage logic
```

## Test Structure

**Suite Organization:**
```js
"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const { TUNING, baseInput, CAR_LIGHT_RWD, CAR_HEAVY_AWD_EV, CAR_MID_RWD_HIGHPI, RANGES } = require("./fixtures.js");
const { inRange, assertAllInRange, assertSpringInPart, assertWhyShape } = require("./helpers.js");

// Precompute shared tunes once at module scope
const L = TUNING.compute(CAR_LIGHT_RWD, "Circuit");

test("tires.front — exact + in range", () => {
  assert.equal(L.tires.front, 29.5);
  for (const t of [L, E, M]) inRange(t.tires.front, RANGES.tirePsi, "tires.front");
});
```

**Patterns:**
- Flat `test("name", () => {...})` calls — no `describe`/nesting; section banners (`/* --- TIRES --- */`) group related tests by comment.
- Tunes are computed once at module scope and reused across many `test()` blocks for speed.
- Test names encode the assertion: `"<param> — exact + in range"`, `"<thing> — <expected behaviour>"`.

## Mocking

**Framework:** None. No mocks, stubs, or spies.

**Approach:**
- The core engine (`tuning.js`) and storage logic (`setups.js`) are pure and DOM-free, so they are tested directly with no test doubles.
- `setups.js` storage logic is validated without touching `localStorage` — functions operate on plain objects/JSON, so no browser mock is needed.

**What to Mock:** Nothing currently. Keep new logic pure so it stays mock-free.

**What NOT to Mock:** The engine itself — exact-value tests depend on real computed output.

## Fixtures and Factories

**Test Data:**
```js
function baseInput(overrides) {
  return Object.assign({ drivetrain: "RWD", power: 400, weight: 3300, /* ...full imperial input... */ }, overrides);
}
// Canonical cars override only what differs:
//   CAR_LIGHT_RWD, CAR_HEAVY_AWD_EV, CAR_MID_RWD_HIGHPI
```
- `baseInput()` mirrors what `app.js readInputs()` produces (imperial units, optional fields as numbers or `null`).
- `RANGES` defines every legal Forza slider min/max and is the source for in-range assertions.

**Location:**
- All fixtures and the engine handle live in `test/fixtures.js`; shared assertions in `test/helpers.js`.

## Coverage

**Requirements:** None enforced. No coverage threshold or CI gate present.

**View Coverage:**
```bash
node --test --experimental-test-coverage
```

## Test Types

**Unit Tests (`unit.test.js`):**
- One test per output parameter across three distinct cars.
- Assert (a) the EXACT value the engine produces (regression contract — captured from the engine, any change must be deliberate) and (b) that the value lands inside its legal slider range.

**Integration Tests (`integration.test.js`):**
- One base car run through all six goals plus the handling-bias slider.
- Directional/relative assertions (e.g. `G.Circuit.arb.front > G.OffRoad.arb.front`) rather than exact values; verifies bias 0 equals the pre-slider baseline.

**Edge Tests (`edge.test.js`):**
- Structural boundary behaviour: FWD emits front-only differential, EV single-speed gearing, rear-engine weight inversion, no-aero suppression, slider-at-0 identity.

**Failure Tests (`failure.test.js`):**
- `validate()` input contract — flags missing/non-finite/out-of-bounds inputs and accepts sane ones.
- Confirms `compute()` never throws and always clamps.

**Invariant Sweep (`sweep.js`):**
- Not a `node:test` file — a standalone fuzz/invariant runner over the full input space (drivetrains × powertrains × engine locations × PI × compounds × suspensions × weights × powers × weight biases).
- Asserts invariants: no NaN/non-finite output, every output in legal range, gear ratios strictly descending, bias 0 == baseline, six goals mutually distinct, drivetrain variation produces distinct tunes. Exits non-zero on failure.

## Common Patterns

**Range Assertion (custom helper):**
```js
function inRange(value, [lo, hi], label) {
  assert.ok(typeof value === "number" && Number.isFinite(value), `${label}: expected finite number`);
  assert.ok(value >= lo && value <= hi, `${label}: ${value} outside [${lo}, ${hi}]`);
}
assertAllInRange(t); // sweeps every numeric output of a tune through its range
```

**Exact-value regression:**
```js
assert.equal(L.gearing.final, 4.34);
assert.deepEqual(L.gearing.ratios, [3.21, 2.05, 1.57, 1.3, 1.13, 1]);
```

**Directional comparison (integration):**
```js
assert.ok(G.Circuit.arb.front > G.OffRoad.arb.front, "front ARB Circuit > OffRoad");
```

**Invalid-input testing:**
```js
function expectInvalid(input, matcher, label) {
  const res = TUNING.validate(input);
  assert.equal(res.valid, false, `${label}: expected invalid`);
  assert.ok(res.errors.some((e) => matcher.test(e)), `${label}: no error matched ${matcher}`);
}
```

---

*Testing analysis: 2026-06-13*
