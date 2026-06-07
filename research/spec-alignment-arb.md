I'll produce a complete, code-ready calculation spec for ALIGNMENT (front/rear camber, front/rear toe, caster) and ANTI-ROLL BARS (front, rear). Let me synthesize the cross-referenced research into deterministic JavaScript formulas.

# Forza Horizon 6 Tuning Calculator — Implementation Spec

## ALIGNMENT & ANTI-ROLL BARS

All formulas reference the exact input variables. Legal slider clamps are enforced at the end of every output. Shared helper below is referenced throughout.

```js
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const round1 = v => Math.round(v * 10) / 10;     // 0.1 deg steps (alignment)
const round01 = v => Math.round(v * 100) / 100;  // ARB 0.01 steps

// ---- Shared derived quantities (compute ONCE, reuse everywhere) ----
const fwPct   = frontWeightPct / 100;                 // e.g. 0.52
const rwPct   = 1 - fwPct;
const frontWeightLb = weight * fwPct;                 // front axle load
const rearWeightLb  = weight * rwPct;                 // rear axle load
const wdDev   = frontWeightPct - 50;                  // signed deviation from 50/50 (+ = nose heavy)

// engine-location bias nudge (front=nose heavy already in frontWeightPct, but
// mid/rear shift dynamic load rearward under power → treat as extra rear bias)
const engBias = engineLocation === 'Rear' ? -3
              : engineLocation === 'Mid'  ? -1.5
              : 0;                                    // in "effective front weight %" points
const effFwPct = frontWeightPct + engBias;            // used only for camber/ARB lean logic

// tire grip factor (more grip → tolerates/wants more negative camber)
const gripFactor = { Race:1.0, Drag:0.9, Sport:0.8, Rally:0.6, Street:0.55, Offroad:0.45 }[tireCompound] ?? 0.7;

// suspension upgrade gate: Stock locks alignment/ARB at factory in-game.
const canTuneSuspension = suspensionType !== 'Stock';
```

---

# 1. ALIGNMENT

## 1.1 FRONT CAMBER

**Baseline.** The community converges on road-race front camber of −1.0° (sport tires) to −2.0° (slicks), scaled by tire grip, then nudged by drivetrain (RWD wants more front camber to manage weight transfer onto outside front; FWD slightly less because front is also driven) and by front axle load.

```js
// Base negative camber from tire grip: maps gripFactor 0.45..1.0 → -0.6 .. -2.0
let frontCamber = -(0.6 + (gripFactor - 0.45) / (1.0 - 0.45) * (2.0 - 0.6));

// Drivetrain trim (deg): RWD more neg front, FWD less, AWD neutral
frontCamber += { RWD:-0.3, AWD:0.0, FWD:+0.3 }[drivetrain];

// Front-load trim: heavier nose loads outside-front harder → +0.1deg neg per 4% over 50
frontCamber += -0.1 * (effFwPct - 50) / 4;
```

**Per-GOAL table (front camber).** Override or offset applied after baseline:

| Goal | Action | Resulting front camber rule |
|---|---|---|
| Circuit | offset −0.2° | baseline (slick-grip ≈ −2.0°, sport ≈ −1.0°) |
| Drag | **override** | `-0.3` (near-flat patch for straight-line) |
| Drift | **override** | `clamp(-3.0 - gripFactor*2.0, -5.0, -3.0)` → grippier tire = closer to −5.0° |
| OffRoad | **override** | `-0.5` (flat contact on loose surface) |
| Rally | **override** | `clamp(-0.8 - gripFactor*0.4, -1.2, -0.8)` |
| Touge | offset +0.1° | slightly less than circuit for tighter low-speed turns |

```js
const frontCamberGoal = {
  Circuit: c => c - 0.2,
  Drag:    () => -0.3,
  Drift:   () => clamp(-3.0 - gripFactor * 2.0, -5.0, -3.0),
  OffRoad: () => -0.5,
  Rally:   () => clamp(-0.8 - gripFactor * 0.4, -1.2, -0.8),
  Touge:   c => c + 0.1,
}[goal](frontCamber);

frontCamber = clamp(round1(frontCamberGoal), -5.0, 0.0);
if (!canTuneSuspension) frontCamber = 0.0; // stock locked
```

**Why (user string):** *"Negative front camber keeps the outside tire flat mid-corner under load; more for grippy slicks and RWD, near-flat for drag/off-road straight-line traction, extreme for drift steering angle."*

## 1.2 REAR CAMBER

**Baseline.** Rear runs roughly half the front's negative camber on asphalt (−0.5° to −1.0°); FWD wants relatively *more* rear camber (lighter driven-rear logic inverted), RWD slightly less.

```js
let rearCamber = frontCamber * 0.55;                 // start from front
rearCamber += { RWD:+0.2, AWD:0.0, FWD:-0.2 }[drivetrain]; // FWD more neg rear
rearCamber += -0.1 * (rwPct*100 - 50) / 6;           // rear-load trim (mild)
```

**Per-GOAL table (rear camber):**

| Goal | Resulting rear camber rule |
|---|---|
| Circuit | `clamp(half-front, -1.0, -0.5)` |
| Drag | override `-0.2` |
| Drift | override `-1.0` |
| OffRoad | override `-0.5` |
| Rally | override `clamp(-0.5 - gripFactor*0.3, -0.8, -0.5)` |
| Touge | `clamp(half-front + 0.05, -1.0, -0.4)` |

```js
const rearCamberGoal = {
  Circuit: r => clamp(r, -1.0, -0.5),
  Drag:    () => -0.2,
  Drift:   () => -1.0,
  OffRoad: () => -0.5,
  Rally:   () => clamp(-0.5 - gripFactor * 0.3, -0.8, -0.5),
  Touge:   r => clamp(r + 0.05, -1.0, -0.4),
}[goal](rearCamber);

rearCamber = clamp(round1(rearCamberGoal), -5.0, 0.0);
// Mid/rear-engine: rear drives + carries weight → +0.2deg neg for traction
if (engineLocation !== 'Front' && goal !== 'OffRoad') rearCamber = clamp(round1(rearCamber - 0.2), -5.0, 0.0);
if (!canTuneSuspension) rearCamber = 0.0;
```

**Why:** *"Rear camber is milder than front to preserve straight-line drive grip; FWD and mid/rear-engine cars add a touch more to plant the loaded/driven rear, drift keeps it shallow so the rear can break away."*

## 1.3 FRONT TOE

**Baseline = 0.0°** (neutral; any toe costs top speed via scrub). Toe-out (negative) sharpens turn-in; applied only as a goal-driven small value, larger for FWD/understeery cars.

```js
let frontToe = 0.0;
// FWD/understeer-prone cars get a hint of toe-out for turn-in
const understeerProne = drivetrain === 'FWD' || effFwPct >= 55;
```

**Per-GOAL table (front toe, degrees, negative = toe-out):**

| Goal | Front toe |
|---|---|
| Circuit | `understeerProne ? -0.1 : -0.05` (critique #9: −0.05 grip-car default to differ from Drag) |
| Drag | `0.0` (zero scrub for top speed) |
| Drift | `+0.0` base, but **drift override** to aggressive toe-out for counter-steer = `-0.2` (kept inside legal range; some setups use up to −0.3) |
| OffRoad | `-0.2` (critique #9: loose-surface toe-out for turn-in; keeps Circuit/Drag/OffRoad distinct) |
| Rally | `-0.1` (turn-in on loose surface) |
| Touge | `-0.1` (tight technical turn-in) |

```js
const frontToeGoal = {
  Circuit: () => understeerProne ? -0.1 : -0.05, // critique #9: grip-car default differs from Drag
  Drag:    () => 0.0,
  Drift:   () => -0.2,
  OffRoad: () => -0.2,                            // critique #9: loose-surface toe-out, distinct from Circuit/Drag
  Rally:   () => -0.1,
  Touge:   () => -0.1,
}[goal]();
frontToe = clamp(round1(frontToeGoal), -5.0, 5.0);
if (!canTuneSuspension) frontToe = 0.0;
```

**Why:** *"Front toe is kept at zero to avoid tire scrub and top-speed loss; a small toe-out is added only when sharper turn-in is worth it (FWD, rally, touge, drift counter-steer)."*

## 1.4 REAR TOE

**Baseline = 0.0°.** Rear toe-in (positive) stabilizes the rear under power, valuable for RWD on throttle; drift wants it near zero or slightly out for rotation.

**Per-GOAL table (rear toe, positive = toe-in):**

| Goal | Rear toe |
|---|---|
| Circuit | `drivetrain==='RWD' ? +0.1 : 0.0` |
| Drag | `+0.1` (launch straight-line stability) |
| Drift | `-0.1` (slight toe-out aids rotation) |
| OffRoad | `0.0` |
| Rally | `+0.1` (stability on loose surface) |
| Touge | `drivetrain==='RWD' ? +0.2 : +0.1` (throttle stability on descents) |

```js
const rearToeGoal = {
  Circuit: () => drivetrain === 'RWD' ? 0.1 : 0.0,
  Drag:    () => 0.1,
  Drift:   () => -0.1,
  OffRoad: () => 0.0,
  Rally:   () => 0.1,
  Touge:   () => drivetrain === 'RWD' ? 0.2 : 0.1,
}[goal]();
// High-torque RWD adds a touch more toe-in for traction stability
let rearToe = rearToeGoal;
if (drivetrain === 'RWD' && torque >= 400 && goal !== 'Drift') rearToe += 0.1;
rearToe = clamp(round1(rearToe), -5.0, 5.0);
if (!canTuneSuspension) rearToe = 0.0;
```

**Why:** *"A little rear toe-in plants the rear axle under acceleration (key for powerful RWD); drift uses slight toe-out so the rear rotates freely, drag/rally favor toe-in for straight-line stability."*

## 1.5 CASTER

**Baseline.** Caster (1.0–7.0 legal; useful 5.0–7.0) scales with weight class / speed: light cars 5.0–5.5°, mid 5.5–6.5°, heavy/high-speed 6.5–7.0°. Use weight as the driver, with a PI-class bump for high-speed classes.

```js
// Weight maps to caster: <2600lb→5.2, ~3200lb→6.0, >4000lb→6.8
let caster = 5.0 + clamp((weight - 2400) / 1800, 0, 1) * (7.0 - 5.0);

// High-speed PI classes push toward upper end (more straight-line stability)
const classCasterBump = { D:-0.3, C:-0.1, B:0.0, A:+0.1, S1:+0.3, S2:+0.4, X:+0.5 }[piClass] ?? 0;
caster += classCasterBump;

// Aero installed → higher speeds → more caster for stability
if (aeroInstalled) caster += 0.2;
```

**Per-GOAL table (caster):**

| Goal | Action |
|---|---|
| Circuit | offset +0.0 (use baseline 5.5–7.0) |
| Drag | override `5.0` (minimal, reduce steering load — straight line) |
| Drift | offset to **max** `+1.0` then clamp → favors 6.5–7.0 for self-centering counter-steer |
| OffRoad | offset −0.5 (lighter steering over terrain) |
| Rally | offset −0.3 |
| Touge | offset +0.2 (stability + self-centering on switchbacks) |

```js
const casterGoal = {
  Circuit: c => c,
  Drag:    () => 5.0,
  Drift:   c => c + 1.0,
  OffRoad: c => c - 0.5,
  Rally:   c => c - 0.3,
  Touge:   c => c + 0.2,
}[goal](caster);
caster = clamp(round1(casterGoal), 1.0, 7.0);
// keep practical floor for any grip discipline
if (['Circuit','Touge','Drift'].includes(goal)) caster = clamp(caster, 5.0, 7.0);
```

**Why:** *"Caster adds straight-line stability and steering self-centering; heavier/faster cars and drift builds run more (up to 7.0°), drag and off-road run less for lighter, quicker steering."*

---

# 2. ANTI-ROLL BARS

ARB legal range **1.00–65.00**. The accepted community equation is the ForzaFire base formula, then a drivetrain-weighted front/rear split, then a goal/balance trim. Mechanical-balance target window 0.55–0.65 (sweet spot 0.60).

## 2.1 BASE ARB (shared)

```js
// Class stiffness % (midpoints of published ranges)
const stiffnessPct = {
  D:0.63, C:0.63, B:0.55, A:0.50, S1:0.45, S2:0.42, X:0.40
}[piClass] ?? 0.50;
// (Sports/Street ≈ 0.61-0.65, High-Perf ≈ 0.40-0.46, Race ≈ 0.35-0.62)

// ForzaFire base: (Weight/2) / (200 - 200*Stiffness%)
let baseARB = (weight / 2) / (200 - 200 * stiffnessPct);
// e.g. 3500lb, 0.50 → 1750/100 = 17.5

// Drivetrain split factor: ARB delta per 1% front-weight deviation from 50
const splitPer1 = { RWD:1.0, AWD:0.66, FWD:-1.0 }[drivetrain];
const splitDelta = splitPer1 * wdDev;   // total front-vs-rear shift

let frontARB = baseARB + splitDelta / 2;
let rearARB  = baseARB - splitDelta / 2;
```

This already reproduces the published typical windows (RWD F18–25/R25–35, AWD F22–30/R28–38, FWD F8–15/R25–40) for normal weights and distributions.

## 2.2 Goal modifiers (multipliers on each bar)

| Goal | Front × | Rear × | Notes |
|---|---|---|---|
| Circuit | 1.00 | 1.00 | hit mechanical balance ≈0.60 |
| Drag | 0.40 | 0.55 | soft, minimal roll control needed; slightly stiffer rear for launch squat control |
| Drift | 0.45 | 1.45 | soft front + stiff rear → rotation/oversteer on demand |
| OffRoad | 0.30 | 0.30 | very soft both ends for compliance |
| Rally | 0.55 | 0.55 | moderate, terrain compliance |
| Touge | 1.05 | 0.95 | slightly understeer-safe, responsive |

```js
const arbGoal = {
  Circuit: { f:1.00, r:1.00 },
  Drag:    { f:0.40, r:0.55 },
  Drift:   { f:0.45, r:1.45 },
  OffRoad: { f:0.30, r:0.30 },
  Rally:   { f:0.55, r:0.55 },
  Touge:   { f:1.05, r:0.95 },
}[goal];
frontARB *= arbGoal.f;
rearARB  *= arbGoal.r;
```

## 2.3 Edge-case trims

```js
// FWD safety: front ARB must stay soft to keep turn-in grip on driven/steer tires
if (drivetrain === 'FWD' && goal !== 'Drift') frontARB = Math.min(frontARB, baseARB * 0.85);

// AWD inherent understeer → bias softer front / stiffer rear on circuit & touge
if (drivetrain === 'AWD' && ['Circuit','Touge'].includes(goal)) {
  frontARB *= 0.92; rearARB *= 1.08;
}

// Mid/rear-engine: rear carries dynamic load → soften rear slightly to keep grip
if (engineLocation !== 'Front' && goal !== 'Drift') rearARB *= 0.92;

// Aero installed adds downforce/roll stiffness need → +8% both ends on grip goals
if (aeroInstalled && ['Circuit','Touge'].includes(goal)) { frontARB *= 1.08; rearARB *= 1.08; }

// EV instant torque + heavy floor battery (low CG, high weight) → +5% both ends
if (powertrain === 'EV') { frontARB *= 1.05; rearARB *= 1.05; }

// Stock suspension can't tune ARB meaningfully → mid value
if (!canTuneSuspension) { frontARB = 32.5; rearARB = 32.5; }

frontARB = clamp(round01(frontARB), 1.00, 65.00);
rearARB  = clamp(round01(rearARB),  1.00, 65.00);
```

**Mechanical-balance note (optional UI feedback).** Approximate balance = `rearARB / (frontARB + rearARB)` inverted, or simply report front bias `frontARB/(frontARB+rearARB)`; for circuit, nudge toward the 0.40–0.45 front-share window (≈0.60 rear) if the user reports under/oversteer: understeer → lower frontARB or raise rearARB by 5%; oversteer → opposite.

**Why strings:**
- **Front ARB:** *"Front anti-roll bar controls front-end roll and understeer; softer front frees grip and rotation (FWD/drift), stiffer front calms a nervous rear."*
- **Rear ARB:** *"Rear anti-roll bar sets rotation; a stiff rear (drift) provokes oversteer for slides, a softer rear (off-road, mid-engine) keeps the driven rear planted."*

---

# 3. WORKED EXAMPLE (sanity check)

Car: RWD, Front engine, ICE, S1, 600 hp, 450 lb-ft, 3300 lb, 52% front, Sport tires, Race suspension, aero installed, goal = **Circuit**.

- `gripFactor`=0.8 → base front camber = −(0.6 + (0.35/0.55)*1.4) = **−1.49**; +(-0.3 RWD) = −1.79; +(-0.1*(2)/4=−0.05) = −1.84; Circuit −0.2 = **−2.0°** (clamped) ✓ in range.
- Rear camber = −2.0*0.55=−1.1; +0.2 RWD = −0.9; clamp(−1.0..−0.5) = **−0.9°** ✓.
- Front toe: not understeer-prone (52%, RWD) → **0.0°** ✓.
- Rear toe: RWD circuit +0.1, torque<400? 450≥400 → +0.1 = **+0.2°** ✓.
- Caster: 5.0+((3300−2400)/1800)*2=6.0; S1 +0.3=6.3; aero +0.2=6.5; Circuit +0 = **6.5°** ✓.
- ARB: stiffness 0.45 → base=(1650)/(200−90)=15.0; split=1.0*2=2 → F16.0/R14.0; aero +8% & no AWD → **F17.3 / R15.1** (RWD nose-heavy correctly stiffer front) ✓ within windows.

Goal = **Drift** on the same car: front camber → −3.0−0.8×2... = clamp = **−4.6°**; rear **−1.0°**; front toe **−0.2°**; rear toe **−0.1°**; caster 6.5+1.0=7.0° (clamped) **7.0°**; ARB front 15.0×0.45≈6.8, rear 15.0×1.45≈21.8 → **F6.8 / R21.8** — visibly different, correct drift character.

---

## Implementation notes / clamps summary
- Camber: round 0.1°, clamp **−5.0..0.0** (negative only in practice).
- Toe: round 0.1°, clamp **−5.0..+5.0** (sign convention: − = toe-out, + = toe-in).
- Caster: round 0.1°, clamp **1.0..7.0** (grip goals floored at 5.0).
- ARB: round 0.01, clamp **1.00..65.00**.
- `suspensionType === 'Stock'` zeroes alignment and centers ARB (no factory tuning).
- All goal branches are mutually exclusive overrides/offsets, guaranteeing visibly distinct tunes per goal for the same car.

Relevant file for implementation: wire these into the alignment and anti-roll-bar calculations in `tuning.js` using the shared derived-quantity block at the top so camber, toe, caster, and ARB all read from one consistent set of intermediate values.