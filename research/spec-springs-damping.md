This is a static HTML/CSS/JS calculator project. I have enough context. The research findings are comprehensive — I'll synthesize them into a concrete, deterministic spec. Let me produce the full specification now.

# FH6 Tuning Calculator — Code-Ready Calculation Spec
## Categories: Springs, Ride Height, Damping

This spec is deterministic JavaScript-ready arithmetic. All formulas reference the exact input variables provided. Where the community lacks a closed-form equation, I convert the accepted heuristic into a fixed formula. Every output is clamped to legal slider ranges at the end.

---

## 0. Shared Derived Quantities (compute once, reuse everywhere)

```js
// ---- Raw inputs (with safe defaults) ----
const weight        = num(input.weight, 3000);        // lb
const frontPct      = clamp(num(input.frontWeightPct, 50), 20, 80) / 100; // 0..1
const rearPct       = 1 - frontPct;
const power         = num(input.power, 300);          // hp
const torque        = num(input.torque, 250);         // lb-ft
const gears         = Math.round(num(input.gears, 6));
const aero          = !!input.aeroInstalled;

// ---- Part-range bounds read from the car's installed parts ----
const springMin     = num(input.springRateMin, 100);  // lb/in
const springMax     = num(input.springRateMax, 1000); // lb/in
const rhMin         = num(input.rideHeightMin, 4.0);   // in
const rhMax         = num(input.rideHeightMax, 7.0);   // in
// IMPLEMENTATION NOTE (2026-06): front and rear part ranges are independent in
// FH6, so the engine reads per-axle keys — springRateMin/MaxF, springRateMin/MaxR,
// rideHeightMin/MaxF, rideHeightMin/MaxR — falling back to the shared keys above
// when an axle value is absent. springF/rideF clamp to the FRONT range; springR/
// rideR clamp to the REAR range. The shared *Min/*Max shorthand below covers the
// (common) symmetric case where both axles use one range.

// ---- Corner / axle weights ----
const frontAxleWeight = weight * frontPct;   // lb carried by front axle
const rearAxleWeight  = weight * rearPct;    // lb carried by rear axle
const frontCornerWt   = frontAxleWeight / 2; // lb per front corner
const rearCornerWt    = rearAxleWeight  / 2; // lb per rear corner

// ---- Engine-location weight-bias nudge (applied to effective frontPct) ----
// Mid/Rear engine pulls mass rearward beyond what frontWeightPct already states;
// used only for spring/damper SPLIT emphasis, never to override real frontPct.
const engBias = { Front: 0.00, Mid: -0.02, Rear: -0.04 }[input.engineLocation] ?? 0;
const effFrontPct = clamp(frontPct + engBias, 0.20, 0.80);

// ---- PI class -> stiffness "tier" used for frequency target & min-bump ----
// D/C = Sports-ish, B/A = High-Performance, S1/S2/R/X = Race.
const classTier = ({ D:'Sports', C:'Sports', B:'HighPerf', A:'HighPerf',
                     S1:'Race', S2:'Race', R:'Race', X:'Race' })[input.piClass] ?? 'HighPerf';

// ---- helpers ----
function num(v,d){ return Number.isFinite(+v) ? +v : d; }
function clamp(v,lo,hi){ return Math.min(hi, Math.max(lo, v)); }
function round1(v){ return Math.round(v*10)/10; }
function round2(v){ return Math.round(v*100)/100; }
```

**Why (corner weights):** spring/damper math is per-axle load; `frontAxleWeight = weight × frontWeightPct` and dividing by 2 gives the load each spring actually supports.

---

## 1. SPRINGS — Front & Rear Spring Rate (lb/in)

### 1.1 Method — natural-frequency target (primary), part-range interpolation (fallback/clamp)

The most credible cross-source method is the **ride-frequency model**: pick a target frequency (Hz) by goal/class, then solve for spring rate from the axle mass. This is the only approach in the research that produces *absolute lb/in numbers* that respect the physics constraint, and it degrades gracefully into the slider-percentage method everyone else quotes.

**Frequency → spring rate (imperial), per axle:**

```
K = (f² × M) / 19.56        // lb/in,  M = sprung mass per CORNER in lb, f = Hz
```
(This is the inverse of the widely-quoted `Hz = sqrt(K / M × 19.56)`; solving for K gives `K = f²·M/19.56`. M is per-corner weight in lb, which approximates the sprung mass closely enough for a game model.)

### 1.2 Baseline target frequencies (Hz) by class + aero

```js
// Base ride frequency by class tier (Hz). Mid-stiffness for grip without skating.
const FREQ_BASE = { Sports: 1.9, HighPerf: 2.2, Race: 2.5 };
let fFront = FREQ_BASE[classTier];
let fRear  = FREQ_BASE[classTier];

// Aero raises usable frequency (downforce compresses suspension): +0.6 Hz both ends.
if (aero) { fFront += 0.6; fRear += 0.6; }

// Suspension hardware ceiling: Stock/Street can't run race-stiff frequencies.
const SUSP_FREQ_CAP = { Stock: 1.6, Street: 2.2, Sport: 2.8,
                        Race: 5.0, Drift: 3.2, Offroad: 2.0 };
const fCap = SUSP_FREQ_CAP[input.suspensionType] ?? 5.0;
```

### 1.3 Per-GOAL frequency modifiers (override/offset)

Each goal sets a front/rear **frequency pair** (Hz) or a multiplier on the class base. These produce visibly different spring rates for the same car.

| Goal | Front Hz | Rear Hz | Rule applied |
|------|----------|---------|--------------|
| **Circuit** | base + 0.0 | base − 0.1 | Stiff, slight front bias for turn-in; class base |
| **Drag** | base − 0.6 | base + 0.2 | Soft front (weight transfer/launch), stiff rear (anti-squat) |
| **Drift** | base − 0.3 | base + 0.4 | Soft-ish front, stiffer rear to kick rotation |
| **Off-Road** | 1.1 (override) | 1.0 (override) | Very soft, absorb terrain |
| **Rally** | base × 0.5 | base × 0.5 | Exactly half race stiffness (community rule) |
| **Touge** | base − 0.1 | base − 0.2 | Slightly softer than circuit for bumpy roads, mechanical grip |

```js
const SPRING_GOAL = {
  Circuit:  { fOff:[ 0.0,-0.1], mul:[1,1],     override:null },
  Drag:     { fOff:[-0.6,+0.2], mul:[1,1],     override:null },
  Drift:    { fOff:[-0.3,+0.4], mul:[1,1],     override:null },
  OffRoad:  { fOff:[ 0,0],      mul:[1,1],     override:[1.1,1.0] },
  Rally:    { fOff:[ 0,0],      mul:[0.5,0.5], override:null },
  Touge:    { fOff:[-0.1,-0.2], mul:[1,1],     override:null },
};
const g = SPRING_GOAL[input.goal] ?? SPRING_GOAL.Circuit;

if (g.override) { fFront = g.override[0]; fRear = g.override[1]; }
else {
  fFront = fFront * g.mul[0] + g.fOff[0];
  fRear  = fRear  * g.mul[1] + g.fOff[1];
}

// Cap by suspension hardware, floor at a drivable 0.8 Hz.
fFront = clamp(fFront, 0.8, fCap);
fRear  = clamp(fRear,  0.8, fCap);
```

### 1.4 Drivetrain spring split (front-heavy vs rear-drive emphasis)

FWD inverts the bias (soft front / stiff rear) per every source. We apply a **frequency split factor** on top of the goal targets, scaled by how far weight deviates from 50/50.

```js
// Weight-deviation magnitude in % points from 50/50.
const devPts = (effFrontPct - 0.5) * 100;   // + = front-heavy

// Drivetrain emphasis on the split (small Hz nudge per the community 4 kgf/mm rule,
// converted to a frequency tweak so absolute math stays consistent).
// RWD/AWD: keep front a touch stiffer; FWD: reverse it.
let splitFront = 0, splitRear = 0;
if (input.drivetrain === 'FWD') { splitFront = -0.10; splitRear = +0.10; }
else if (input.drivetrain === 'AWD') { splitFront = +0.05; splitRear = +0.05; }
else /* RWD */ { splitFront = +0.05; splitRear = -0.05; }

fFront = clamp(fFront + splitFront, 0.8, fCap);
fRear  = clamp(fRear  + splitRear,  0.8, fCap);
```

### 1.5 Solve spring rates, then clamp to the car's part range

```js
let frontSpring = (fFront * fFront * frontCornerWt) / 19.56;
let rearSpring  = (fRear  * fRear  * rearCornerWt)  / 19.56;

// Weight-distribution fine adjust (community: ~15 lb/in per 1% off 50/50),
// nudges the SPLIT a little harder for very lopsided cars.
frontSpring += 15 * Math.max(0, devPts) * (input.drivetrain==='FWD' ? -1 : 1) * 0.5;
rearSpring  -= 15 * Math.max(0, devPts) * (input.drivetrain==='FWD' ? -1 : 1) * 0.5;

// EV: heavy floor battery lowers CoG & adds sprung mass -> +8% stiffness both ends.
if (input.powertrain === 'EV')     { frontSpring *= 1.08; rearSpring *= 1.08; }
if (input.powertrain === 'Hybrid') { frontSpring *= 1.04; rearSpring *= 1.04; }

// Clamp into the installed part range. If the frequency target falls outside the
// part range, fall back to interpolation anchors so the slider still lands sensibly.
frontSpring = clamp(frontSpring, springMin, springMax);
rearSpring  = clamp(rearSpring,  springMin, springMax);

// Physics sanity: total support must exceed vehicle weight at min ride height.
// (Front×RH×2)+(Rear×RH×2) >= weight ; if violated, raise both toward springMax.
const support = (frontSpring + rearSpring) * 2 * rhMin;
if (support < weight) {
  const scale = weight / support;
  frontSpring = clamp(frontSpring * scale, springMin, springMax);
  rearSpring  = clamp(rearSpring  * scale, springMin, springMax);
}

frontSpring = Math.round(frontSpring);
rearSpring  = Math.round(rearSpring);
```

### 1.6 Edge cases (springs)

| Condition | Effect |
|-----------|--------|
| **FWD** | Front/rear frequency split inverted (−0.10/+0.10): soft front, stiff rear for rotation. |
| **RWD** | Slight front-stiff bias (+0.05/−0.05) to fight power-on squat understeer. |
| **AWD** | Both ends +0.05 (planted, can run stiffer); weight-dev applied at full 15 lb/in. |
| **Mid/Rear engine** | `engBias` shifts `effFrontPct` down 2–4%, automatically softening front / stiffening rear via the corner-weight term. |
| **EV / Hybrid** | +8% / +4% stiffness (extra battery mass, lower CoG tolerates it). |
| **Stock/Street suspension** | Frequency capped at 1.6 / 2.2 Hz → springs stay soft even on Circuit. |
| **Race suspension** | Cap 5.0 Hz → full stiffness range unlocked. |
| **Aero installed** | +0.6 Hz both ends before goal mods. |
| **No part range (springMin==springMax)** | Output = that single value (locked part). |

**User-facing why:** *"Spring rate is solved from a target ride frequency (~{fFront.toFixed(1)} Hz front / {fRear.toFixed(1)} Hz rear) for your class and goal, scaled to the {frontCornerWt|0} lb resting on each front corner — stiffer for grip/aero, softer for {goal} compliance."*

---

## 2. RIDE HEIGHT — Front & Rear (inches)

### 2.1 Method — slider-fraction of the car's `rideHeightMin/Max`

No source gives an equation; all give a **position fraction** of the part range per discipline. We turn each discipline into a deterministic fraction `p ∈ [0,1]`, then map: `height = rhMin + (rhMax - rhMin) × p`.

### 2.2 Per-GOAL fraction table (front / rear)

| Goal | Front frac | Rear frac | Rule |
|------|-----------|-----------|------|
| **Circuit** | 0.00 | 0.00 | Min both ends → lowest CoG |
| **Drag** | 0.00 | 0.30 | Min front, raised rear = forward rake for launch weight transfer |
| **Drift** | 0.05 | 0.05 | Near-min both ends, responsiveness |
| **Off-Road** | 1.00 | 1.00 | Max both ends, terrain clearance |
| **Rally** | 0.75 | 0.75 | 70–80% of range for travel + clearance |
| **Touge** | 0.05 | 0.05 | Low both ends, slightly off floor for road bumps |

```js
const RH_GOAL = {
  Circuit: [0.00, 0.00],
  Drag:    [0.00, 0.30],
  Drift:   [0.05, 0.05],
  OffRoad: [1.00, 1.00],
  Rally:   [0.75, 0.75],
  Touge:   [0.05, 0.05],
};
let [pF, pR] = RH_GOAL[input.goal] ?? RH_GOAL.Circuit;
```

### 2.3 Modifiers (additive to the fraction, then clamp 0..1)

```js
// Tire compound terrain bias: dirt/rally/offroad compounds want more clearance.
const COMPOUND_RH = { Street:0, Sport:0, Race:0, Drag:0, Rally:+0.10, Offroad:+0.15 };
const rhComp = COMPOUND_RH[input.tireCompound] ?? 0;
pF = clamp(pF + rhComp, 0, 1);
pR = clamp(pR + rhComp, 0, 1);

// Aero on a low ride height needs a hair of rake control: nudge rear +0.05 on tarmac goals.
if (aero && ['Circuit','Touge','Drag'].includes(input.goal)) pR = clamp(pR + 0.05, 0, 1);

// Bottoming guard: if springs are soft AND goal is low, lift both a touch.
// Soft = frontSpring < 0.25 of springMax range above min.
const softFront = (frontSpring - springMin) < 0.25 * (springMax - springMin);
if (softFront && pF < 0.15) { pF = clamp(pF + 0.10, 0, 1); pR = clamp(pR + 0.10, 0, 1); }

// Heavy car (>4000 lb) on low setting: +0.05 to avoid grounding.
if (weight > 4000) { pF = clamp(pF + 0.05, 0, 1); pR = clamp(pR + 0.05, 0, 1); }
```

### 2.4 Map to inches and clamp

```js
let frontRH = rhMin + (rhMax - rhMin) * pF;
let rearRH  = rhMin + (rhMax - rhMin) * pR;

frontRH = round1(clamp(frontRH, rhMin, rhMax));
rearRH  = round1(clamp(rearRH,  rhMin, rhMax));
```

### 2.5 Edge cases (ride height)

| Condition | Effect |
|-----------|--------|
| **Drag** | Rear raised 30% of range (rake) regardless of drivetrain — launch weight transfer. |
| **Off-Road / Rally** | Max / 75% of range; compound bonus can push to ceiling. |
| **Rally/Offroad tire on a tarmac goal** | +0.10–0.15 fraction so you don't bottom on mixed surface. |
| **Aero + low goal** | Rear +0.05 to protect rear diffuser/wing angle and add stability. |
| **Soft springs + min height** | Auto +0.10 both ends (anti-bottoming). |
| **EV/Hybrid** | No special rule (covered by weight>4000 guard for heavy battery cars). |
| **rhMin==rhMax** | Output = locked value. |

**User-facing why:** *"Ride height sits at {(pF*100)|0}% of your suspension's travel — {goal} wants it {pF<0.2?'as low as possible for a low center of gravity':'raised for clearance and suspension travel'}{drag?', with a forward rake (higher rear) to plant the drive wheels on launch':''}."*

---

## 3. DAMPING — Front/Rear Rebound & Front/Rear Bump (0.0–20.0)

### 3.1 Method — weight-derived bump, fixed 60% bump:rebound ratio, spring-diff rear offset

This is the most consistently documented formula across sources:

```
FrontBump    = MinBump(class) + (frontAxleWeight_lb / 200) × 0.1
FrontRebound = FrontBump / 0.6
RearBump     = FrontBump    + rearOffsetBump(springDiff%)
RearRebound  = FrontRebound + rearOffsetRebound(springDiff%)
```

### 3.2 Class minimum bump

```js
const MIN_BUMP = { Sports: 4.6, HighPerf: 4.7, Race: 4.8 }[classTier];
```

### 3.3 Spring-rate difference → rear damper offset

```js
const springDiffPct = Math.abs(frontSpring - rearSpring) /
                      Math.max(frontSpring, rearSpring, 1) * 100;

function rearOffset(diff){
  if (diff <= 1.5) return { reb:-0.2, bump:-0.1 };
  if (diff <= 35 ) return { reb:-0.3, bump:-0.2 };
  if (diff <= 40 ) return { reb:-0.6, bump:-0.4 };
  return                  { reb:-1.2, bump:-0.8 };
}
const off = rearOffset(springDiffPct);
```

### 3.4 Baseline computation

```js
let fBump = MIN_BUMP + (frontAxleWeight / 200) * 0.1;
let fReb  = fBump / 0.6;
let rBump = fBump + off.bump;   // offsets are negative -> rear slightly softer
let rReb  = fReb  + off.reb;
```

### 3.5 Per-GOAL damping modifiers

Goals shift the **overall stiffness** and the **bump:rebound ratio**. Drift/Off-Road soften dramatically; Rally adds rebound for terrain recovery.

| Goal | Bump ×mult | Rebound ×mult | Rebound +offset | Notes |
|------|-----------|---------------|-----------------|-------|
| **Circuit** | 1.00 | 1.00 | 0 | Formula baseline (~60% ratio) |
| **Drag** | 1.10 | 1.05 | 0 | Slightly firmer to control transfer; front rebound soft handled by drivetrain rule |
| **Drift** | 0.55 | 0.55 | 0 | Soft everywhere (~4/4) for predictable transitions |
| **Off-Road** | 0.30 | 0.45 | +1.0 | Very soft bump, compliant; +1.0 rebound for recovery |
| **Rally** | 0.55 | 0.65 | +1.0 | Half-stiffness springs → softer dampers; +1.0 rebound |
| **Touge** | 0.85 | 0.90 | 0 | A touch softer than circuit for road bumps |

```js
const DAMP_GOAL = {
  Circuit: { bMul:1.00, rMul:1.00, rAdd:0.0 },
  Drag:    { bMul:1.10, rMul:1.05, rAdd:0.0 },
  Drift:   { bMul:0.55, rMul:0.55, rAdd:0.0 },
  OffRoad: { bMul:0.30, rMul:0.45, rAdd:1.0 },
  Rally:   { bMul:0.55, rMul:0.65, rAdd:1.0 },
  Touge:   { bMul:0.85, rMul:0.90, rAdd:0.0 },
};
const dg = DAMP_GOAL[input.goal] ?? DAMP_GOAL.Circuit;

fBump *= dg.bMul; rBump *= dg.bMul;
fReb   = fReb*dg.rMul + dg.rAdd;
rReb   = rReb*dg.rMul + dg.rAdd;
```

### 3.6 Drivetrain & engine-location damper trim

```js
// FWD: stiffer front damping (drives + steers + brakes); RWD: stiffer rear control.
if (input.drivetrain === 'FWD') { fBump+=0.5; fReb+=0.5; rBump-=0.3; rReb-=0.3; }
else if (input.drivetrain === 'RWD') { rBump+=0.3; rReb+=0.3; }
else /* AWD */ { fBump+=0.2; fReb+=0.2; rBump+=0.2; rReb+=0.2; }

// Drag-specific asymmetry (RWD): soft front rebound + firm front bump for squat/launch.
if (input.goal === 'Drag' && input.drivetrain === 'RWD') {
  fReb  = Math.max(fReb - 2.0, 1.0);  // let nose rise on launch
  fBump = fBump + 1.0;                // resist front compression off the line
  rReb  = rReb + 1.0;                 // control the planted rear
}

// Mid/Rear engine: extra rear mass -> +0.4 rear damping to settle it.
if (input.engineLocation !== 'Front') { rBump+=0.4; rReb+=0.4; }

// EV instant torque + mass: +0.3 rebound both ends to catch the heavier body.
if (input.powertrain === 'EV') { fReb+=0.3; rReb+=0.3; }
```

### 3.7 Off-road/Rally suspension floor & clamps

```js
// Offroad/Rally hardware lets bump drop near minimum for big hits.
if (['OffRoad','Rally'].includes(input.goal) || input.suspensionType==='Offroad'){
  fBump = Math.max(1.0, fBump); rBump = Math.max(1.0, rBump);
}

// Enforce legal 0.0–20.0 and keep bump within 40–70% of rebound (physics sanity).
fBump = clamp(fBump, 1.0, 20.0); rBump = clamp(rBump, 1.0, 20.0);
fReb  = clamp(fReb,  1.0, 20.0); rReb  = clamp(rReb,  1.0, 20.0);

// Ratio guard: if bump > 70% of rebound, lower bump; if < 40%, raise it.
function ratioFix(b, r){
  const lo = 0.40*r, hi = 0.70*r;
  return clamp(b, lo, hi);
}
// Skip ratio guard for Drag-RWD (intentionally asymmetric).
if (!(input.goal==='Drag' && input.drivetrain==='RWD')) {
  fBump = ratioFix(fBump, fReb);
  rBump = ratioFix(rBump, rReb);
}

const out = {
  frontBump:    round1(fBump),
  rearBump:     round1(rBump),
  frontRebound: round1(fReb),
  rearRebound:  round1(rReb),
};
```

### 3.8 Edge cases (damping)

| Condition | Effect |
|-----------|--------|
| **FWD** | +0.5 front bump/rebound, −0.3 rear → front-biased control. |
| **RWD** | +0.3 rear damping for traction/squat control. |
| **AWD** | +0.2 all corners (planted, can carry more damping). |
| **Drag + RWD** | Asymmetric: front rebound −2.0 (nose lifts), front bump +1.0, rear rebound +1.0; ratio guard bypassed. |
| **Mid/Rear engine** | +0.4 rear bump & rebound to settle rear mass. |
| **EV** | +0.3 rebound both ends (heavier body, instant torque transfer). |
| **Off-Road/Rally** | Big soften via multipliers, +1.0 rebound, bump floored at 1.0. |
| **Spring diff > 40%** | Rear rebound −1.2 / bump −0.8 (large front/rear stiffness gap). |
| **All goals** | Final clamp 1.0–20.0; bump held to 40–70% of rebound. |

**User-facing why:** *"Bump is derived from the {frontAxleWeight|0} lb on the front axle and set to ~60% of rebound (the universal compromise between absorbing bumps and controlling body motion); {goal} {dg.bMul<0.7?'softens everything for compliance':'keeps it firm for a stable platform'}, and your {drivetrain} drivetrain biases damping toward the {drivetrain==='FWD'?'front':'rear'}."*

---

## 4. Worked Example (sanity check)

**Car:** RWD, Front engine, ICE, A-class, 3200 lb, 54% front, Race slicks, Race suspension, aero installed, goal = Circuit. Part ranges: spring 150–1100 lb/in, RH 3.5–5.5 in.

- `frontAxleWeight = 3200×0.54 = 1728` → `frontCornerWt = 864`; `rearCornerWt = 736`.
- classTier = HighPerf → FREQ_BASE 2.2; aero +0.6 → 2.8; Circuit fOff [0,−0.1] → fFront 2.8, fRear 2.7; RWD split +0.05/−0.05 → 2.85 / 2.65 (cap 5.0 ok).
- `frontSpring = 2.85²×864/19.56 = 358`; `rearSpring = 2.65²×736/19.56 = 264`. Support `(358+264)×2×3.5=4354 ≥ 3200` ✓ → **Front 358, Rear 264 lb/in**.
- Ride height Circuit [0,0] → **Front 3.5, Rear 3.5 in** (aero+circuit nudges rear +0.05 frac → 3.6).
- MinBump HighPerf 4.7; `fBump = 4.7 + 1728/200×0.1 = 4.87`; `fReb = 8.12`; springDiff `|358−264|/358=26%` → off −0.3/−0.2 → `rBump 4.67, rReb 7.82`. Circuit mult 1.0; RWD +0.3 rear → **fBump 4.9, fReb 8.1, rBump 5.0, rReb 8.1** (after round + ratio guard).

Switching the same car to **Drift** yields softer springs (rear-biased), min ride height, and ~4/4 dampers — visibly different, as required.

---

## 5. Implementation Notes
- Compute in the order: **Springs → Ride Height → Damping** (damping consumes spring values; ride-height bottoming-guard consumes spring values).
- All outputs already clamped to legal slider ranges (spring within part range; ride height within part range; damping 0.0–20.0).
- `round1` for ride height/damping (one decimal matches the game slider), integer for spring rate, matching Forza's lb/in slider granularity.

**Relevant project files:** `index.html`, `styles.css` (this spec is implemented in `tuning.js`).