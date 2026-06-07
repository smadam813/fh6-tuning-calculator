Now I have all the research context I need. Let me synthesize the findings into a precise, code-ready specification. The research is internally consistent on the key numbers, with the main conflict being baseline PSI (two camps: ForzaFire's "31-32.5 hot" school vs. the "26-28 cold" school). I'll resolve this in favor of FH6-specific ForzaFire/skycoach numbers and treat values as cold-set targets per the calculator's stated convention.

# Forza Horizon 6 Tuning Calculator — Spec: TIRES & BRAKING

This spec is **deterministic and code-ready**. All formulas reference the exact input variables listed and clamp to legal slider ranges. Where the community has only heuristics, I convert them into closed-form arithmetic.

---

## Shared Derived Quantities (compute once, reuse everywhere)

```js
// ---- Derived inputs used by both categories ----
const frontWeightFrac = clamp(frontWeightPct, 30, 75) / 100;        // 0.30–0.75
const rearWeightFrac  = 1 - frontWeightFrac;
const frontAxleWeight = weight * frontWeightFrac;                    // lb on front axle
const rearAxleWeight  = weight * rearWeightFrac;                     // lb on rear axle

// Engine-location weight-bias nudge (front% already reflects most of it,
// but mid/rear engines shift dynamic balance further rearward under load)
const engBias = engineLocation === 'Rear' ? -0.02
              : engineLocation === 'Mid'  ? -0.01
              : 0;                                                    // applied to brake-bias only

// Powertrain torque character (EV = instant torque, more wheelspin / lock risk)
const evFactor = powertrain === 'EV'     ? 1
               : powertrain === 'Hybrid' ? 0.5
               : 0;                                                   // 0..1

// PI class index 0..6 (D..X), used for "high-performance +0.5 PSI" rule
const PI_INDEX = { D:0, C:1, B:2, A:3, S1:4, S2:5, X:6 };
const piIdx = PI_INDEX[piClass] ?? 3;

// Universal clamp helper
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const round1 = (v) => Math.round(v * 10) / 10;       // tire psi & brakes step 0.5/1
const roundHalf = (v) => Math.round(v * 2) / 2;      // snap to 0.5
```

---

# 1. TIRES — Front & Rear Cold PSI

**Legal range:** 15.0–55.0 psi. Calculator convention = cold set pressure. Adjustment granularity = 0.5 psi.

### 1.1 Baseline by tire compound (cold-set target)

The two source camps disagree (~26–28 "cold" school vs. ~31–32.5 "hot/FH6" school). Resolved by treating these as **cold targets** and taking the FH6-specific ForzaFire/Skycoach numbers as canonical, lightly lowered to cold-set values (≈ −2 psi off the hot targets they quote, which matches forzatune's "cold 27–30 → warm 32–34" relationship). This keeps grip-compound cars near the community sweet spot of ~28–30 cold.

```js
// Cold-set baseline PSI per compound (mean front=rear before splits)
const TIRE_BASE_PSI = {
  Street:  29.0,
  Sport:   29.5,
  Race:    30.0,   // semi-slick / race slick
  Rally:   27.0,
  Drag:    30.0,   // overridden hard by goal split below
  Offroad: 24.0,   // off-road / cross-country, lower for contact patch
};
let basePsi = TIRE_BASE_PSI[tireCompound] ?? 29.0;
```

**Why:** Softer race compounds run stiffer sidewalls and a hotter operating window, so they take a few psi more than rally/off-road rubber which needs a bigger, softer contact patch on loose ground.

### 1.2 Weight adjustment (heavier car → higher psi)

Sources converge on **~+1 psi per 600 kg ≈ per 1323 lb** above a reference, with light kei cars ~3–5 psi below heavy muscle cars on the same compound. Reference mass = 3000 lb.

```js
// +1.0 psi per 1323 lb (600 kg) deviation from 3000 lb reference, capped ±5
const weightPsiAdj = clamp((weight - 3000) / 1323 * 1.0, -5, 5);
basePsi += weightPsiAdj;
```

**Why:** A heavier car deflects the tire more, so it needs more pressure to keep the contact patch from over-flexing and overheating.

### 1.3 High-performance class bonus

Community rule: "High Performance / Race / Prototype / GP cars add ~+0.5 psi." Map to PI class A and above.

```js
if (piIdx >= PI_INDEX.A) basePsi += 0.5;   // A, S1, S2, X
```

**Why:** High-PI cars carry more cornering load and aero, so a touch more pressure stabilizes the sidewall.

### 1.4 Tire-width / aero proxy

Wider tires (more grip) and downforce add ~+0.5 psi. We approximate width by PI class beyond A and by `aeroInstalled`.

```js
let frontPsi = basePsi;
let rearPsi  = basePsi;
if (aeroInstalled) { frontPsi += 0.5; rearPsi += 0.5; }     // stiffer sidewall under DF load
if (piIdx >= PI_INDEX.S1) { frontPsi += 0.5; rearPsi += 0.5; } // proxy for wide sticky tires
```

### 1.5 Drivetrain front/rear split

Strong source consensus:

| Drivetrain | Front offset vs rear |
|---|---|
| FWD | front **+1.5** psi (steering + power axle) |
| RWD | front **+0.75** psi |
| AWD | front **+0.35** psi (near-equal) |

```js
const TIRE_SPLIT = { FWD: 1.5, RWD: 0.75, AWD: 0.35 };
const split = TIRE_SPLIT[drivetrain] ?? 0.5;
frontPsi += split / 2;
rearPsi  -= split / 2;
```

**Why:** The front axle does the steering (FWD also the driving), so slightly higher front pressure sharpens turn-in and resists rollover of the contact patch.

### 1.6 Engine-location refinement

Mid/rear-engine cars carry rear mass → bleak a little of the front bias back.

```js
if (engineLocation === 'Mid')  { frontPsi += 0.25; rearPsi -= 0.25; }
if (engineLocation === 'Rear') { frontPsi += 0.5;  rearPsi -= 0.5;  } // rear loaded, front light
```

**Why:** With weight behind the axle line, the rear tire is already loaded; lowering its pressure enlarges its patch for traction while the light front gets a bit more.

### 1.7 Per-GOAL modifier table (applied last, before clamps)

| Goal | Front Δ | Rear Δ | Override / Notes |
|---|---|---|---|
| **Circuit** | +0.0 | +0.0 | keep computed grip baseline |
| **Drag** | **set high** | **set low** | `frontPsi = base+5`, `rearPsi = max(15, base−8)` (max front / min rear for launch weight transfer) |
| **Drift** | **override** | **override** | `frontPsi = 30`, `rearPsi = 27` (low overall for predictable slip; front slightly higher) |
| **OffRoad** | −2.0 | −2.0 | bigger soft patch on loose surface |
| **Rally** | −1.0 | −1.0 | compliance on uneven dirt |
| **Touge** | +0.5 | +0.5 | firmer for sharp transitions on tight tarmac |

```js
switch (goal) {
  case 'Drag':
    // Drag is RWD-launch oriented: huge rear patch, firm steer front.
    frontPsi = basePsi + 5.0;
    rearPsi  = basePsi - 8.0;
    // For FWD drag (drive=front) invert the launch axle:
    if (drivetrain === 'FWD') { frontPsi = basePsi - 8.0; rearPsi = basePsi + 5.0; }
    break;
  case 'Drift':
    frontPsi = 30.0; rearPsi = 27.0;   // low-grip, controllable
    break;
  case 'OffRoad':
    frontPsi -= 2.0; rearPsi -= 2.0; break;
  case 'Rally':
    frontPsi -= 1.0; rearPsi -= 1.0; break;
  case 'Touge':
    frontPsi += 0.5; rearPsi += 0.5; break;
  case 'Circuit':
  default: break;
}
```

### 1.8 Final clamps & rounding

```js
frontPsi = roundHalf(clamp(frontPsi, 15.0, 55.0));
rearPsi  = roundHalf(clamp(rearPsi,  15.0, 55.0));
```

### 1.9 Worked examples

- **A-class RWD coupe, 3300 lb, 53% front, Race tires, no aero, Circuit:** base 30.0 +weight(0.23)→30.2 +HP(0.5)→30.7; split RWD → F 31.1 / R 30.3 → **F 31.0 / R 30.5 psi**.
- **Same car, Drag goal:** F = 30.7+5 = 35.7 → **35.5** / R = 30.7−8 = 22.7 → **22.5 psi** (rounded to 0.5, clamped).
- **600 kg (1323 lb) FWD kei, C-class, Sport, Rally goal:** base 29.5 +weight(−1.27)→28.2; FWD split F+0.75/R−0.75 → F 29.0/R 27.5; Rally −1 → **F 28.0 / R 26.5 psi**.

### 1.10 User-facing "why" strings

- Front: *"Higher front pressure than rear sharpens steering response on the {drivetrain} drive layout; {compound} compound and {weight} lb car set the {frontPsi} psi baseline."*
- Rear: *"Lower rear pressure enlarges the contact patch for traction; tuned for {goal} on {compound} tires."*

---

# 2. BRAKING — Brake Balance (% front) & Brake Pressure (%)

**Legal ranges:** balance 0–100% front; pressure 0–200%. Granularity: balance 1%, pressure 5%.

## 2.A Brake Balance (% front)

### 2.A.1 Drivetrain baseline (front bias %)

Tight source consensus:

| Drivetrain | Baseline front bias % |
|---|---|
| RWD | **52** |
| AWD | **54** |
| FWD | **58** |

```js
const BRAKE_BASE = { RWD: 52, AWD: 54, FWD: 58 };
let brakeBias = BRAKE_BASE[drivetrain] ?? 53;
```

**Why:** FWD carries its mass and grip up front so it tolerates (and needs) more front braking; RWD wants a more rearward split to use all four tires under forward weight transfer.

### 2.A.2 Weight-distribution correction

Multiple sources give a weight-based formula: bias tracks static weight, nudged ~half a percent per 1% off 50/50. We fold actual `frontWeightPct` in so a nose-heavy car gets more front brake.

```js
// +0.5% front bias per 1% front weight above 50, ±6% cap
brakeBias += clamp((frontWeightPct - 50) * 0.5, -6, 6);
```

**Why:** Under braking, weight pitches forward; the more static front weight, the more braking the front axle can absorb without locking.

### 2.A.3 Engine-location correction

```js
// Mid/rear engine → shift bias rearward (less front) to avoid light-rear lockup
brakeBias += engBias * 100 * 0.5;   // engBias is -0.02/-0.01/0  → -1.0 / -0.5 / 0 %
```

**Why:** A rear-mounted engine keeps weight over the rear axle even while braking, so a slightly more rearward split balances lock-up.

### 2.A.4 Powertrain (EV regen / instant torque) correction

```js
// EVs brake regeneratively on the driven axle; add tiny front bias for stability,
// scaled by hybrid/EV factor. (Friction-brake balance still tunable.)
brakeBias += evFactor * 1.0;        // EV +1%, Hybrid +0.5%, ICE +0%
```

**Why:** EV regen loads the driven axle on lift-off; a hair more front friction bias keeps the rear from over-slowing into a corner.

### 2.A.5 Tire/suspension compound correction

```js
// Low-grip surfaces (rally/offroad tires) lock easily → pull bias toward 50 for balance
if (tireCompound === 'Rally' || tireCompound === 'Offroad') brakeBias -= 3;
```

### 2.A.6 Per-GOAL modifier table

| Goal | Balance modifier | Resulting character |
|---|---|---|
| **Circuit** | +0 | computed, stability-first |
| **Drag** | +3 (front) | straight-line stability, no lockup at speed |
| **Drift** | **override → 48** front (i.e. rear-biased) | rear brake helps rotate/initiate without front lock |
| **OffRoad** | −4 (toward rear) | avoid front-wash on loose ground |
| **Rally** | −3 (toward rear) | rotate the car on dirt, jump-landing stability |
| **Touge** | −1 | slight rear bias for trail-brake rotation |

```js
const BRAKE_BIAS_GOAL = { Circuit:0, Drag:+3, Drift:null, OffRoad:-4, Rally:-3, Touge:-1 };
if (goal === 'Drift') brakeBias = 48;          // hard override (rear-biased for rotation)
else brakeBias += BRAKE_BIAS_GOAL[goal] ?? 0;
```

> Note: sources split on drift bias (some say 45–50 rear-biased, others 55–70 front for *tandem* control). For a single-driver simulation calculator, **48% (rear-biased) is the safer default** — it rotates the car and prevents front-wash mid-angle. Expose as the chosen heuristic.

### 2.A.7 Clamp & round

```js
brakeBias = Math.round(clamp(brakeBias, 40, 65));   // sane racing window inside 0–100 legal
```

**Why we clamp 40–65 not 0–100:** anything outside this is undriveable; the slider allows 0–100 but no tune wants the extremes.

## 2.B Brake Pressure (%)

### 2.B.1 Baseline

```js
let brakePressure = 100;   // game default = balanced full-modulation
```

### 2.B.2 Deterministic adjustments

The community rule is "100% default, 105–115 aggressive (heavy cars), 85–95 soft (light cars / trail-brake)." Turn into arithmetic on weight, tire grip, and EV torque:

```js
// Heavier cars need more clamping force; lighter cars need finesse.
brakePressure += clamp((weight - 3000) / 1000 * 5, -10, 12);   // ±~10% by mass

// Grip compound can take more pressure without lockup; low-grip wants less.
const GRIP_PRESS = { Race:+5, Sport:+2, Street:0, Drag:+5, Rally:-5, Offroad:-8 };
brakePressure += GRIP_PRESS[tireCompound] ?? 0;

// EV instant-stop & regen → trim friction pressure slightly to avoid lockup
brakePressure -= evFactor * 3;     // EV −3, Hybrid −1.5
```

### 2.B.3 Per-GOAL modifier table

| Goal | Pressure modifier | Why |
|---|---|---|
| **Circuit** | +5 | hard threshold braking |
| **Drag** | −10 | minimal braking demand, avoid lockup |
| **Drift** | −5 | finesse, modulation over angle |
| **OffRoad** | −10 | loose surface locks easily, soft for control |
| **Rally** | −5 | modulated braking on dirt |
| **Touge** | +3 | firm but modulated for trail-braking |

```js
const PRESS_GOAL = { Circuit:+5, Drag:-10, Drift:-5, OffRoad:-10, Rally:-5, Touge:+3 };
brakePressure += PRESS_GOAL[goal] ?? 0;
```

### 2.B.4 Clamp & round

```js
brakePressure = Math.round(clamp(brakePressure, 80, 130) / 5) * 5;  // step 5, sane 80–130 in 0–200 legal
```

**Why clamp 80–130:** below 80% brakes feel dead; above 130% provokes instant lockup. Legal range is 0–200 but no useful tune leaves this band.

### 2.B.5 Worked examples

- **AWD A-class, 3400 lb, 55% front, Race tires, ICE, Circuit:** bias 54 +weight((55−50)*0.5=+2.5)→56.5 +grip(0) → 56.5 → **57% front**. Pressure 100 +mass((400/1000)*5=+2)→102 +Race(+5)→107 +Circuit(+5)→112 → **110%**.
- **RWD S2, 2900 lb, 48% front, Race, EV, Mid-engine, Touge:** bias 52 +(48−50)*0.5=−1 →51 +engBias(−0.5)→50.5 +EV(+1)→51.5 +Touge(−1)→50.5 → **51% front**. Pressure 100 +mass(−0.5)→99.5 +Race(+5)→104.5 +EV(−3)→101.5 +Touge(+3)→104.5 → **105%**.
- **FWD C-class hatch, 2600 lb, 62% front, Sport, Rally goal:** bias 58 +(62−50)*0.5=+6(cap)→64 +Rally(−3)→61, tire rally? no (Sport) → **61% front**. Pressure 100 +mass(−2)→98 +Sport(+2)→100 +Rally(−5)→95 → **95%**.

### 2.B.6 User-facing "why" strings

- Balance: *"{brakeBias}% front balance suits {drivetrain} with {frontWeightPct}% front weight — enough front brake to use forward weight transfer without locking the rear under {goal} braking."*
- Pressure: *"{brakePressure}% pressure is tuned so the tires lock only in the last ~10–15% of trigger pull on {compound} tires — firm enough to stop hard, soft enough to modulate."*

---

## Implementation summary (drop-in order)

1. Compute shared derived quantities (`frontWeightFrac`, axle weights, `engBias`, `evFactor`, `piIdx`).
2. **Tires:** compound base → weight adj → HP bonus → aero/width → drivetrain split → engine-loc → goal table → clamp(15–55, step 0.5).
3. **Brakes balance:** drivetrain base → weight-dist → engine-loc → EV → low-grip → goal table/override (Drift=48) → clamp(40–65, step 1).
4. **Brakes pressure:** 100 base → mass → grip compound → EV → goal table → clamp(80–130, step 5).

All four outputs (frontPsi, rearPsi, brakeBias, brakePressure) are pure deterministic functions of the listed inputs, vary visibly per goal, respect every legal slider range, and carry a one-line rationale each. Key conflict resolutions: cold-set PSI baseline chosen over hot-target school; drift brake bias set rear-biased (48%) per the single-driver-rotation heuristic; drag tire split made drivetrain-aware (launch axle gets the big patch).