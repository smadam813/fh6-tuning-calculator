No existing code. This is a greenfield spec task. I have the full research corpus. Let me synthesize it into the implementation spec for the two requested categories (AERO and DIFFERENTIAL).

The research is consistent and rich for both categories. I'll now produce the complete, code-ready specification.

---

# FH6 Tuning Calculator — Spec: AERO & DIFFERENTIAL

All formulas below assume these derived intermediates are computed once and reused:

```js
// ---- Shared derived inputs (compute once) ----
const frontWeightFrac = clamp(frontWeightPct, 20, 80) / 100;   // 0.20..0.80
const rearWeightFrac  = 1 - frontWeightFrac;
const frontCornerWt   = weight * frontWeightFrac;              // lb on front axle
const rearCornerWt    = weight * rearWeightFrac;               // lb on rear axle
const powerToWeight   = power / weight;                        // hp per lb
const isHighPower     = power >= 600;                          // aero/diff scaling trigger

function clamp(v, lo, hi) { return Math.min(hi, Math.max(lo, v)); }
function round2(v, step) { return Math.round(v / step) * step; }   // snap to slider step
function roundEven(v) { return Math.round(v / 2) * 2; }            // diff accel uses 2% steps
```

Forza part ranges are per-car, so for downforce we work against the part's `aeroFrontMin/Max` and `aeroRearMin/Max` (in lbf). When those are unknown, use the fallback reference maxima noted in each section.

---

## CATEGORY 1: AERO (front & rear downforce, lbf)

### 1.1 Core model

FH6 exposes an **Aero Balance** stat = `frontDF / (frontDF + rearDF)`. The community workflow is: pick a target balance by drivetrain, pick an overall downforce *level* by goal, then solve the two sliders. We invert the balance equation to get concrete lbf.

**Step A — Skip if no aero.** If `aeroInstalled === false`, both outputs are `null`/"no aero part installed". Front splitter-only cars (common on street builds) → rear output is `null`, only front is set.

**Step B — Overall downforce level (0..1 fraction of each slider's usable range), by goal.** This sets *how much* total grip-vs-speed you trade.

```js
// downforceLevel = fraction of the slider span you push toward max
// applied to BOTH ends before balance correction
const AERO_LEVEL = {
  Circuit: 0.85,   // near-max, time-attack grip
  Touge:   0.55,   // moderate: corner grip but keep straight speed
  Drift:   0.30,   // low; rotation freedom, mostly front splitter
  Rally:   0.15,   // aero ~useless under ~150 km/h on dirt
  OffRoad: 0.05,   // effectively off
  Drag:    0.00    // minimize drag entirely
};
```

**Step C — Target Aero Balance, by drivetrain** (front share of total downforce):

```js
function aeroBalanceTarget(drivetrain, goal) {
  let b = { FWD: 0.50, RWD: 0.525, AWD: 0.425 }[drivetrain]; // mid of researched windows
  // RWD 0.50-0.55 -> 0.525 ; AWD 0.40-0.45 -> 0.425 ; FWD 0.45-0.55 -> 0.50
  if (goal === 'Drift') b = clamp(b - 0.025, 0.45, 0.50); // slightly more front for predictable rear breakaway
  if (goal === 'Drag')  b = 0.50;                          // irrelevant; keep neutral, level≈0 anyway
  return b;
}
```

**Step D — Solve the two sliders from level + balance.**

Let each end's usable span be `[min,max]`. Compute a provisional symmetric downforce at the chosen level, then redistribute to hit the balance target while staying in range.

```js
function computeAero({ aeroInstalled, hasRearWing, drivetrain, goal, weight,
                       frontMin, frontMax, rearMin, rearMax, frontWeightPct, power }) {
  if (!aeroInstalled) return { frontDF: null, rearDF: null,
    why: "No aero part installed; downforce not tunable." };

  const level = AERO_LEVEL[goal];
  const balance = aeroBalanceTarget(drivetrain, goal);

  // provisional magnitude at this level (use rear span as the scale reference)
  const frontSpan = frontMax - frontMin;
  const rearSpan  = rearMax  - rearMin;

  // base each end at level fraction of its own span
  let frontDF = frontMin + frontSpan * level;
  let rearDF  = rearMin  + rearSpan  * level;

  // --- balance correction: nudge toward target front share ---
  // total kept ~constant; shift between ends to reach `balance`
  let total = frontDF + rearDF;
  let targetFront = total * balance;
  let targetRear  = total * (1 - balance);
  frontDF = clamp(targetFront, frontMin, frontMax);
  rearDF  = clamp(targetRear,  rearMin,  rearMax);

  // --- weight-bias trim (heavier-loaded end wants a touch more DF) ---
  // +1.87 lbf rear per 1% front-weight above 47% reference (research: balanced-rear formula)
  const rearTrim = (frontWeightPct - 47) * 1.87;
  rearDF = clamp(rearDF + rearTrim, rearMin, rearMax);

  // --- high-power scaling: >=600 hp benefits from more total DF for stability ---
  if (power >= 600 && goal !== 'Drag' && goal !== 'OffRoad') {
    const k = 1 + Math.min((power - 600) / 600, 0.5) * 0.5; // up to +25% by ~1200 hp
    frontDF = clamp(frontDF * k, frontMin, frontMax);
    rearDF  = clamp(rearDF  * k, rearMin,  rearMax);
  }

  // --- drivetrain hard shapes (AWD meta: max front / min rear) ---
  if (drivetrain === 'AWD' && goal === 'Circuit') {
    frontDF = frontMax;                       // AWD fights understeer with max front
    rearDF  = clamp(rearMin + rearSpan*0.15, rearMin, rearMax); // near-min rear
  }

  // --- engine-location trim: rear/mid engine = more rear grip already -> ease rear DF ---
  if (engineLocation === 'Rear') rearDF = clamp(rearDF - rearSpan*0.10, rearMin, rearMax);
  if (engineLocation === 'Mid')  rearDF = clamp(rearDF - rearSpan*0.05, rearMin, rearMax);

  // --- Drag override: floor both ---
  if (goal === 'Drag') { frontDF = frontMin; rearDF = rearMin; }

  // --- splitter-only car ---
  if (!hasRearWing) rearDF = null;

  return {
    frontDF: round2(frontDF, 1),
    rearDF:  rearDF === null ? null : round2(rearDF, 1),
    aeroBalance: rearDF === null ? 1.0
                 : +(frontDF / (frontDF + rearDF)).toFixed(3)
  };
}
```

### 1.2 Fallback reference maxima (when part ranges unknown)

Use the research's standard-kit reference anchored on a 2700 lb baseline; scale by weight:

```js
// Standard Forza aero kit reference (lbf), weight-adjusted:
//   adj = (2700 - weight) / 100
const adj = (2700 - weight) / 100;
const frontMaxRef = clamp(165 - adj, 30, 300);  // typical front splitter max region
const rearMaxRef  = clamp(300 - adj, 50, 520);  // typical rear wing max region
const frontMinRef = 30, rearMinRef = 50;        // part minimums
```

### 1.3 Per-GOAL modifier table — AERO

| Goal | Level (×span) | Balance target (front share) | Override / notes |
|---|---|---|---|
| **Circuit** | 0.85 | DT default (RWD .525 / AWD .425 / FWD .50) | AWD: force front=max, rear≈min+15% span |
| **Touge** | 0.55 | DT default | Balance corner grip vs straight speed |
| **Drift** | 0.30 | DT default −0.025 (more front) | Prefer splitter; if no wing, rear=null |
| **OffRoad** | 0.05 | DT default | Aero negligible <150 km/h; near-zero |
| **Rally** | 0.15 | DT default | Low; dirt speeds rarely benefit |
| **Drag** | 0.00 | 0.50 (n/a) | **Override both ends to part min** (kill drag) |

### 1.4 Edge cases — AERO

- **No aero installed** → outputs `null`; show "Install an aero part to tune downforce."
- **Splitter-only (no rear wing)** → rear output `null`, balance reported as `1.0`.
- **AWD** → front-biased (0.40–0.45), and in Circuit clamp to max-front/min-rear (the dominant meta; AWD's rear traction makes rear DF mostly drag).
- **RWD** → neutral-to-rear (0.50–0.55); add rear if high-speed oversteer reported.
- **FWD** → front-biased (0.45–0.55); front handles steering+power.
- **Engine location** → Rear/Mid engine already loads rear tires, so shed 5–10% of rear span.
- **EV/Hybrid** → no aero difference; EVs are usually heavy, so the weight-scaling term naturally raises DF — correct behavior.
- **High power (≥600 hp)** → scale total DF up to +25% for high-speed stability (skip for Drag/OffRoad).
- **Clamp** every output to `[partMin, partMax]`, snap to 1 lbf step.

### 1.5 "Why" strings — AERO

- Front: *"Front downforce set for {goal}: more front bite fights understeer; {drivetrain} targets a {balance} aero-balance front share."*
- Rear: *"Rear downforce balances the car at {aeroBalance} front share — lower means more straight-line speed, higher means more high-speed stability."*
- Drag: *"Downforce floored to minimize drag for maximum top speed."*
- AWD circuit: *"AWD runs max front / near-min rear: the rear already has drive traction, so rear wing would only add drag."*

---

## CATEGORY 2: DIFFERENTIAL

Outputs: **accel lock %**, **decel lock %** (per relevant axle), and for **AWD** a **center balance % (rear torque share)**. Forza accel uses **2% increments** (even numbers); decel/center can use 1%. All clamped 0–100; AWD center floored at 50.

### 2.1 Baselines by drivetrain (Circuit reference, then goal-modified)

```js
// Base Circuit values, resolved from cross-referenced ranges (mid-points):
const DIFF_BASE = {
  RWD: { accel: 55, decel: 20 },                 // 40-65 / 15-30 -> mid
  FWD: { accel: 60, decel: 8  },                 // 50-70 / 5-10  -> mid (research splits; use ~60/8)
  AWD: {
    front:  { accel: 30, decel: 5  },            // road race front loose
    rear:   { accel: 80, decel: 30 },            // rear grippier
    center: 78                                    // % torque to REAR (70-85 -> 78)
  }
};
```

### 2.2 Power-sensitive accel trim (deterministic heuristic)

High-power cars spin up; reduce accel lock so the inside wheel can give. Low-power cars want more lock for drive.

```js
// trim in percentage points, applied to accel only, then re-snapped to even
function accelPowerTrim(power, weight) {
  const pw = power / weight;          // hp/lb
  // reference ~0.13 hp/lb (e.g. 400hp / 3000lb). Each +0.05 over ref -> -6 pts accel.
  const delta = (pw - 0.13) / 0.05;   // signed
  return clamp(-delta * 6, -16, 10);  // strong power loosens, weak tightens
}
```

### 2.3 Full compute

```js
function computeDifferential({ drivetrain, goal, power, weight, frontWeightPct,
                               engineLocation, tireCompound, suspensionType }) {
  const trim = accelPowerTrim(power, weight);   // ± accel points
  const G = DIFF_GOAL[goal];                    // see table below

  if (drivetrain === 'RWD') {
    let accel = DIFF_BASE.RWD.accel + trim + G.rwd.accelAdj;
    let decel = DIFF_BASE.RWD.decel + G.rwd.decelAdj;
    if (G.rwd.accelOverride != null) accel = G.rwd.accelOverride;
    if (G.rwd.decelOverride != null) decel = G.rwd.decelOverride;
    // rear/mid engine already plants rear -> can ease accel slightly
    if (engineLocation !== 'Front') accel -= 4;
    // low-grip surface tires want less lock for rotation control
    if (['Rally','Offroad'].includes(tireCompound)) accel -= 6;
    return { rearAccel: clampEven(accel), rearDecel: clampDec(decel) };
  }

  if (drivetrain === 'FWD') {
    let accel = DIFF_BASE.FWD.accel + trim + G.fwd.accelAdj;
    let decel = DIFF_BASE.FWD.decel + G.fwd.decelAdj;
    if (G.fwd.accelOverride != null) accel = G.fwd.accelOverride;
    accel = clamp(accel, 0, 95);  // FWD hard cap 95 (above kills turn-in)
    decel = clamp(decel, 5, 100); // FWD decel floor 5 (below = instability)
    return { frontAccel: clampEven(accel), frontDecel: clampDec(decel) };
  }

  // AWD: three diffs
  let fAccel = DIFF_BASE.AWD.front.accel + trim*0.5 + G.awd.frontAccelAdj;
  let fDecel = DIFF_BASE.AWD.front.decel + G.awd.frontDecelAdj;
  let rAccel = DIFF_BASE.AWD.rear.accel  + trim     + G.awd.rearAccelAdj;
  let rDecel = DIFF_BASE.AWD.rear.decel  + G.awd.rearDecelAdj;
  let center = G.awd.centerOverride != null ? G.awd.centerOverride
                                            : DIFF_BASE.AWD.center + G.awd.centerAdj;

  // weight-bias: front-heavy AWD wants more rear center bias to fight push
  center += clamp((frontWeightPct - 50) * 0.4, -6, 8);

  // long-wheelbase / high-power tolerate up to 90% rear
  if (power >= 600) center += 3;

  return {
    frontAccel: clamp(clampEven(fAccel), 0, 95),
    frontDecel: clampDec(clamp(fDecel, 0, 100)),
    rearAccel:  clampEven(clamp(rAccel, 0, 100)),
    rearDecel:  clampDec(clamp(rDecel, 0, 100)),
    centerRear: clamp(Math.round(center), 50, 90)   // never below 50% rear
  };
}

function clampEven(v){ return roundEven(clamp(v,0,100)); } // accel: even %, 0-100
function clampDec(v){  return Math.round(clamp(v,0,100)); } // decel: 1% ok
```

### 2.4 Per-GOAL modifier table — DIFFERENTIAL

All values are **additive points** unless `Override` is given. (Adj applied to the base before clamps.)

#### RWD (single rear diff)

| Goal | accelAdj | decelAdj | Override | Resulting ~accel/decel (mid car) |
|---|---|---|---|---|
| **Circuit** | 0 | 0 | — | 55 / 20 |
| **Drag** | — | — | accel=90, decel=0 | 90 / 0 (max launch hookup, decel irrelevant) |
| **Drift** | — | — | accel=100, decel=30 | 100 / 30 (near-welded) |
| **OffRoad** | +24 → cap | decel=20 | accel clamps | ~79 / 20 (loose-surface drive) |
| **Rally** | +12 | +5 | — | ~67 / 25 |
| **Touge** | +18 | −2 | — | ~73 / 18 (corner-exit punch) |

#### FWD (single front diff)

| Goal | accelAdj | decelAdj | Override | Resulting ~accel/decel |
|---|---|---|---|---|
| **Circuit** | 0 | 0 | — | 60 / 8 |
| **Drag** | — | — | accel=90 | 90 / 8 |
| **Drift** | — | — | accel=90 | 90 / 8 (FWD "drift" = pivot) |
| **OffRoad** | +20 | +7 | — | ~80 / 15 |
| **Rally** | +15 | +7 | — | ~75 / 15 |
| **Touge** | +10 | 0 | — | ~70 / 8 |

#### AWD (front / rear / center-rear%)

| Goal | F accel | F decel | R accel | R decel | center-rear% |
|---|---|---|---|---|---|
| **Circuit** | base 30 | 5 | 80 | 30 | **78** (70–85) |
| **Drag** | +10 | 0 | +20→cap | 0 | **85** (rear launch) |
| **Drift** | +20 | +10 | →100 | +20 | **88** (max rear) |
| **OffRoad** | +10 (→~40) | +8 (→~13) | −10 (→70) | −8 (→22) | **60** (55–65) |
| **Rally** | +5 (→~35) | +5 (→~10) | −15 (→65) | −8 (→22) | **70** (65–75) |
| **Touge** | −10 (→~20) | 0 | −5 (→75) | −10 (→20) | **80** (rear-biased agility) |

Expressed as the `DIFF_GOAL` object:

```js
const DIFF_GOAL = {
  Circuit: { rwd:{accelAdj:0,decelAdj:0}, fwd:{accelAdj:0,decelAdj:0},
             awd:{frontAccelAdj:0,frontDecelAdj:0,rearAccelAdj:0,rearDecelAdj:0,centerAdj:0} },
  Drag:    { rwd:{accelOverride:90,decelOverride:0}, fwd:{accelOverride:90},
             awd:{frontAccelAdj:10,frontDecelAdj:0,rearAccelAdj:20,rearDecelAdj:0,centerOverride:85} },
  Drift:   { rwd:{accelOverride:100,decelOverride:30}, fwd:{accelOverride:90},
             awd:{frontAccelAdj:20,frontDecelAdj:10,rearAccelAdj:30,rearDecelAdj:20,centerOverride:88} },
  OffRoad: { rwd:{accelAdj:24,decelOverride:20}, fwd:{accelAdj:20,decelAdj:7},
             awd:{frontAccelAdj:10,frontDecelAdj:8,rearAccelAdj:-10,rearDecelAdj:-8,centerOverride:60} },
  Rally:   { rwd:{accelAdj:12,decelAdj:5}, fwd:{accelAdj:15,decelAdj:7},
             awd:{frontAccelAdj:5,frontDecelAdj:5,rearAccelAdj:-15,rearDecelAdj:-8,centerOverride:70} },
  Touge:   { rwd:{accelAdj:18,decelAdj:-2}, fwd:{accelAdj:10,decelAdj:0},
             awd:{frontAccelAdj:-10,frontDecelAdj:0,rearAccelAdj:-5,rearDecelAdj:-10,centerOverride:80} }
};
```

### 2.5 Edge cases — DIFFERENTIAL

- **Drivetrain routing**: RWD → only `rearAccel/rearDecel`. FWD → only `frontAccel/frontDecel`. AWD → all five (front A/D, rear A/D, center-rear%).
- **AWD center floor**: never below **50%** rear (research hard rule); cap at **90%** (only long-wheelbase/high-power reaches it).
- **FWD hard caps**: accel ≤ **95** (above destroys turn-in), decel ≥ **5** (below = instability).
- **Accel increment**: snap to **even %** (2% Forza steps). Decel/center may use 1%.
- **Powertrain**:
  - **EV** — instant torque + (usually) heavier → more wheelspin risk: subtract an extra **6 accel pts** on RWD/FWD, **4** on AWD rear. EVs are frequently AWD dual-motor; keep center logic but bias slightly more rear (+3) since rear motor dominates launch.
  - **Hybrid** — like ICE but slight low-end torque bump: subtract **3 accel pts**.
  - **ICE** — baseline.
- **Engine location**: Rear/Mid RWD already plants the rear → −4 accel pts (less lock needed for traction; preserves rotation).
- **Tire/suspension**: low-grip compounds (`Rally`/`Offroad`) or `Offroad`/`Drift` suspension → −6 accel pts so the diff doesn't overwhelm available grip; `Race` slicks tolerate the full base value.
- **Power scaling**: `accelPowerTrim` loosens high-power cars (down to −16 pts) and tightens low-power (up to +10 pts) automatically.
- **Decel meaning**: only acts off-throttle; high decel = stable entry but lift-off understeer; low = lift-off oversteer. Drift wants high decel for slide control; Circuit keeps it moderate.

Apply the powertrain accel adjustment inside each branch before the final `clampEven`:

```js
const ptAccel = powertrain === 'EV' ? -6 : powertrain === 'Hybrid' ? -3 : 0;
// add ptAccel to each axle's accel; for AWD rear use ptAccel, AWD front use ptAccel*0.5
```

### 2.6 "Why" strings — DIFFERENTIAL

- Accel (RWD/rear): *"Acceleration lock {x}%: higher lock puts power down on corner exit; {goal} and your {power} hp/{weight} lb set this — too high causes exit understeer, too low spins the inside wheel."*
- Decel: *"Deceleration lock {x}%: controls off-throttle stability into corners; higher = calmer entry, lower = more rotation on lift."*
- AWD center: *"Center balance {centerRear}% to the rear: keeps the AWD rear-biased so it rotates instead of pushing; floored at 50% and raised for {goal}."*
- FWD: *"Front accel {x}% (capped at 95): enough lock to put power down without killing turn-in on a front-driven car."*
- Drift override: *"Near-welded {x}% accel for a locked rear axle that holds angle predictably."*
- Drag override: *"Max accel, zero decel: lock both rear wheels for a straight, even launch."*

---

## Worked example (sanity check)

**Car:** RWD, Front engine, ICE, A-class, 450 hp, 3000 lb, 52% front, Race slicks, Sport susp, aero installed (front 30–165, rear 50–300), goal **Circuit**.

- **Aero**: level 0.85, balance RWD 0.525. front=30+135×.85=144.75, rear=50+250×.85=262.5 → total 407 → targetFront=213.7→clamp 165, targetRear=193.3. rearTrim=(52−47)×1.87=+9.4→202.7. No high-power scale. → **Front ≈ 165 lbf, Rear ≈ 203 lbf**, balance ≈ 0.448. *(Front-limited by part max; reads near-max front, moderate rear — correct circuit shape.)*
- **Diff**: trim = ((0.15−0.13)/0.05)×−6 = −2.4. accel=55−2.4+0=52.6→ even **52%**. decel=20→ **20%**. Front engine, race tires → no extra trims. → **Rear accel 52% / decel 20%.**

Same car, goal **Drift** → Aero level 0.30 (front≈80, rear≈131, splitter-only would null rear); Diff → **accel 100% / decel 30%** (near-welded). Same car **Drag** → Aero **floored 30 / 50**; Diff **accel 90% / decel 0%**. The three goals yield visibly different tunes, as required.

---

## Implementation notes / files

These two specs are self-contained: implement `computeAero(...)` and `computeDifferential(...)` as pure functions taking the documented input object, plus the shared `clamp/round2/roundEven/clampEven/clampDec` helpers and the `AERO_LEVEL`, `DIFF_BASE`, `DIFF_GOAL` constant tables. All outputs are clamped to legal Forza ranges (downforce to part min/max in lbf; diff 0–100%, AWD center 50–90%, accel snapped to even %).