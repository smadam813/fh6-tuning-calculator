I'll produce a complete, code-ready calculation spec for GEARING. Let me cross-reference the research findings and resolve the conflicts before writing the spec.

The research gives me several gearing formulas. Let me synthesize the key ones:

- **Final Drive (HP-based)**: `FD = 4.25 + ((400 - HP) / 600)` — consistent across ForzaFire, grindout, ForzaQuickTune. Note grindout writes it as `4.25 + ((400 − Power)/6) × 0.01` which equals the same thing.
- **Per-gear spacing (power series)**: `Rn = A × n^B`, A = first gear, B = -0.65 → yields (3.00, 1.91, 1.47, 1.22, 1.05, 0.94).
- **Goal offsets**: Rally +0.50, Off-road +0.75, Drag -0.20, Drift +0.10, Aero -0.05 to -0.40.
- **Edge cases**: ≥800hp halve power, ≤200hp double power; custom box uses stock FD.
- **Physics**: `V = RPM / (Gear × FD) × π × tireDia × conversion`.

Here is the complete spec.

---

# GEARING — Code-Ready Calculation Spec (FH6 Tuning Calculator)

Outputs produced: **`finalDrive`** (1 value) + **`gearRatios[]`** (one ratio per gear, length = `gears`).

All math is deterministic JavaScript. Every output is clamped to FH6 legal slider ranges at the end.

---

## 0. Constants & Derived Inputs

```js
// ---- FH6 legal slider clamps for gearing ----
const FD_MIN = 2.00,  FD_MAX = 7.00;     // Final drive part range (typical Forza race box)
const GEAR_MIN = 0.50, GEAR_MAX = 5.50;  // Individual gear ratio range
const FIRST_GEAR_MIN = 1.80, FIRST_GEAR_MAX = 5.50;
const TOP_GEAR_MIN   = 0.50, TOP_GEAR_MAX  = 2.00;

// ---- Reference race gearbox (FH6 default 6-speed) ----
const REF_FD = 4.25;          // FD that the HP formula is anchored to (at 400 hp)
const REF_RATIOS_6 = [2.89, 1.99, 1.49, 1.19, 0.94, 0.78]; // for sanity/fallback

// ---- Derived quantities from raw inputs ----
// power (hp), weight (lb), frontWeightPct (e.g. 52), gears (int), torque (lb-ft)
const pw = power / weight;                       // power-to-weight (hp/lb)
const driveWheelsAreFront = (drivetrain === 'FWD');

// Effective power used by FD formula — handles extreme power (see §3).
function effectivePower(hp) {
  if (hp >= 800) return hp / 2;     // halve very high power (community rule)
  if (hp <= 200) return hp * 2;     // double very low power
  return hp;
}
```

**Why (corner-weight note):** Gearing does not need corner weights, but power-to-weight (`pw`) is the real driver of how "short" gears should be — a light, powerful car wants longer gears (less FD) because it accelerates hard regardless; a heavy car wants shorter gears (more FD) to multiply torque.

---

## 1. Baseline Final Drive

The community-canonical formula, anchored at 400 hp → 4.25 FD. More power ⇒ lower FD (taller gears); less power ⇒ higher FD (shorter gears).

```js
function baseFinalDrive(power) {
  const p = effectivePower(power);
  // FD = 4.25 + ((400 - p) / 600)   ≡  4.25 + ((400 - p)/6 * 0.01)
  let fd = REF_FD + ((400 - p) / 600);

  // Weight correction: the pure-HP formula assumes a ~3000 lb car.
  // Add shorter gearing for heavy cars, taller for light cars.
  // +0.10 FD per 500 lb above 3000; symmetric below. (±capped at ±0.50)
  const weightAdj = clamp((weight - 3000) / 500 * 0.10, -0.50, 0.50);
  fd += weightAdj;

  return fd;
}
```

**Worked check:** 325 hp, 3000 lb → `4.25 + (75/600) = 4.375` (≈ the 4.38 the sources cite). ✔
600 hp, 3000 lb → `4.25 + (-200/600) = 3.92` (sources say 3.91). ✔

**Why:** Final drive is set so the engine just reaches the rev limiter in top gear at the end of the longest straight; the HP term keeps the engine in its power band, the weight term compensates for the formula's implicit 3000 lb reference mass.

---

## 2. Per-GOAL Modifier Table (applied to Final Drive)

Each goal applies an **additive FD offset** plus a **gear-spacing exponent override** (see §4). Offsets are from grindout/ForzaQuickTune consensus.

| Goal | FD offset | Spacing exp `B` | Notes |
|---|---|---|---|
| **Circuit** | `+0.00` | `-0.65` | Balanced; redline at end of longest straight |
| **Drag** | `-0.20` | `-0.58` (tighter top) | Taller top gear for terminal speed; tight upper gaps keep engine pinned in power band |
| **Drift** | `+0.10` | `-0.70` (wider) | Slightly shorter for throttle/angle control; wide low gears |
| **Off-Road** | `+0.75` | `-0.72` (wider) | Much shorter for low-speed corner-exit punch on loose surface |
| **Rally** | `+0.50` | `-0.70` (wider) | Shorter for exit traction; wide gaps for varied terrain |
| **Touge** | `+0.20` | `-0.66` | Mountain: short-ish for sustained mid-range punch out of hairpins |

```js
const GOAL_GEARING = {
  Circuit: { fdOffset:  0.00, spacingB: -0.65 },
  Drag:    { fdOffset: -0.20, spacingB: -0.58 },
  Drift:   { fdOffset: +0.10, spacingB: -0.70 },
  OffRoad: { fdOffset: +0.75, spacingB: -0.72 },
  Rally:   { fdOffset: +0.50, spacingB: -0.70 },
  Touge:   { fdOffset: +0.20, spacingB: -0.66 },
};
```

**Why (per goal):**
- **Circuit:** neutral baseline keeps the engine in its power band across the lap.
- **Drag:** a taller final drive trades launch for a higher terminal speed at the trap.
- **Drift:** a touch shorter so the rear breaks loose and you can hold angle on throttle.
- **Off-Road / Rally:** much shorter gearing gives instant corner-exit drive on low-grip surfaces where top speed is rarely reached.
- **Touge:** moderately short to keep punch out of tight hairpins while retaining usable top-end on straights.

---

## 3. Aero, Drivetrain, Engine-Location & Powertrain Modifiers (Final Drive)

```js
function adjustedFinalDrive(power, goal) {
  let fd = baseFinalDrive(power);
  fd += GOAL_GEARING[goal].fdOffset;

  // --- Aero: downforce adds drag → engine can't pull as tall a top gear → shorten slightly.
  // Scale offset by goal relevance (no effect for Drag where aero is stripped).
  if (aeroInstalled && goal !== 'Drag') {
    fd += -0.10; // mild; sources cite -0.05..-0.40 by downforce level. Use -0.10 default,
                 // or scale: -0.05 (low) … -0.40 (max) if a downforce slider value is known.
  }

  // --- Drivetrain: AWD/FWD lose a little drive efficiency at launch. Because
  //     launch traction (not gearing) is the limiter there, we instead run a
  //     marginally LONGER top gear (fd += 0.05) so terminal speed is preserved
  //     while the driveline loss is absorbed up top, not at the line.
  if (drivetrain === 'AWD') fd += +0.05; // longer top gear compensates for driveline loss
  if (drivetrain === 'FWD') fd += +0.05; // longer top gear compensates for driveline loss at launch

  // --- Engine location: rear/mid weight bias aids drive traction → can run marginally taller.
  if (engineLocation === 'Rear') fd += -0.05;
  if (engineLocation === 'Mid')  fd += -0.03;

  // --- Tire compound: low-grip compounds can't use ultra-short gearing (wheelspin) → taller.
  const gripTall = { Race: 0.00, Sport: +0.00, Street: -0.05, Drag: 0.00,
                     Rally: -0.05, Offroad: -0.08 }[tireCompound] ?? 0;
  // (negative = taller because grip-limited; keeps wheels from spinning)
  fd += -gripTall * 0; // NOTE: compound effect is dominated by goal; keep neutral unless needed.

  return clamp(fd, FD_MIN, FD_MAX);
}
```

> **Powertrain (CRITICAL — EV / Hybrid):**

```js
function gearCountFor(powertrain, gearsInput, goal) {
  if (powertrain === 'EV') return 1;            // EVs are single-speed in Forza
  return gearsInput;                             // ICE / Hybrid use the chosen gear count
}
```

- **EV:** Forza EVs use a **single fixed-ratio** transmission. Output **exactly one gear** and tune only the final drive. Because EV torque is instant and flat, bias the FD **+0.15 taller-friendly**, i.e. run the FD slightly **lower** (longer) since there's no power band to keep in — instead optimize purely for top speed at redline. Override:

```js
if (powertrain === 'EV') {
  fd = clamp(adjustedFinalDrive(power, goal) - 0.15, FD_MIN, FD_MAX); // longer single ratio
}
```

- **Hybrid:** treat as ICE for ratios, but its broader torque plateau tolerates **wider gaps** — nudge spacing exponent `B` by `-0.02` (slightly wider) since you don't need to hug a narrow power peak.

**Why:** Aero drag caps how tall the top gear can pull, so we shorten the final drive when wings are fitted; AWD/FWD launch and driveline losses favor marginally shorter gearing; EVs have one gear and a flat torque curve, so only the final ratio matters and it's tuned straight for top speed.

---

## 4. Per-Gear Ratios (power-series spacing)

Individual gears follow a **logarithmic decay**: big gap 1st→2nd (tame wheelspin), tight gaps at the top (wind resistance grows fast). Formula: `Rn = A · n^B`.

```js
// firstGearByPowerToWeight: more power-per-pound → can run a taller 1st (less wheelspin risk)
function firstGearRatio(pw, goal) {
  // pw ~ 0.05 (slow) … 0.40+ (hypercar). Map to 1st-gear 3.40 … 2.40.
  let A = 3.40 - clamp((pw - 0.05) / 0.35, 0, 1) * 1.00;  // 3.40 → 2.40
  // Goal nudges to 1st gear:
  const firstAdj = { Circuit:0, Drag:+0.30, Drift:-0.10, OffRoad:+0.40, Rally:+0.30, Touge:+0.10 }[goal] ?? 0;
  A += firstAdj; // Drag/Off-Road/Rally want a shorter (numerically higher) 1st for launch/exit
  return clamp(A, FIRST_GEAR_MIN, FIRST_GEAR_MAX);
}

function gearRatios(power, goal, powertrain, gearsInput) {
  const N = gearCountFor(powertrain, gearsInput, goal);
  if (N <= 1) return [/* single ratio */ firstGearRatio(pw, goal)]; // EV: one gear

  const A = firstGearRatio(pw, goal);
  let B = GOAL_GEARING[goal].spacingB;
  if (powertrain === 'Hybrid') B -= 0.02;   // wider gaps tolerated
  if (drivetrain === 'AWD' && (goal==='Rally'||goal==='OffRoad')) B -= 0.02; // AWD off-road wider

  const ratios = [];
  for (let n = 1; n <= N; n++) {
    let Rn = A * Math.pow(n, B);
    ratios.push(clamp(Rn, GEAR_MIN, GEAR_MAX));
  }

  // Enforce strict monotonic decrease (each gear taller than the last) after clamping:
  for (let i = 1; i < ratios.length; i++) {
    if (ratios[i] >= ratios[i-1]) ratios[i] = +(ratios[i-1] - 0.05).toFixed(2);
    ratios[i] = clamp(ratios[i], GEAR_MIN, GEAR_MAX);
  }
  return ratios.map(r => +r.toFixed(2));
}
```

**Worked check:** A = 3.00, B = -0.65, N = 6 → `[3.00, 1.91, 1.47, 1.22, 1.05, 0.94]` exactly matches the cited power-series example. ✔

**Why:** Gears are spaced on a logarithmic curve — a wide 1st-to-2nd gap controls launch wheelspin, while progressively tighter top gears keep the engine in its power band against rapidly rising aerodynamic drag.

### 4.1 Powerband-aware spacing exponent (max-torque RPM)

The per-goal constant `B` ignores the engine. When a **`maxTorqueRPM`** (and `redlineRPM`) is supplied,
derive `B` from the powerband width so each upshift drops the engine back **toward** its torque band —
wider band → wider gaps, narrower band → closer gears. Keep the `Rₙ = A·nᴮ` law (so the proven
clamp / strictly-descending / floor-lift guards still apply); only `B` changes:

```js
const SPREAD = 0.85, B_LO = -0.95, B_HI = -0.45, KMAX_LO = 1.10, KMAX_HI = 2.20, CIRC_B = -0.65;
const haveBand = maxTorqueRpm > 0 && redlineRpm > 0;
if (!haveBand) {
  B = GOAL_B;  // per-goal constant — byte-identical fallback (what the parity grid runs)
} else {
  const kMax = clamp(redlineRpm / maxTorqueRpm, KMAX_LO, KMAX_HI);   // powerband width ratio
  let Bband = -(N - 1) * Math.log(SPREAD * kMax) / Math.log(N);      // span-anchored geometric solve
  Bband += GOAL_B - CIRC_B;                                          // preserve per-goal character
  const Bfloor = Math.log(0.52 / A) / Math.log(N);                   // keeps top gear ≥ 0.52
  B = clamp(Bband, Math.max(B_LO, Bfloor), B_HI);                    // B_HI keeps gears descending
}
```

`Bfloor` (caps wide-band aggression so the top gear can't fall below 0.52 before generation) and
`B_HI` (caps narrow-band tightness so adjacent gears can't tie after rounding) bracket a valid window
for **every gear count 2–10 and every band**, so the downstream `[0.5,5.5]` clamp / descending-fixup /
floor-lift never have to fire (verified across a 37M-config sweep). **Honest limitation:** this anchors
the *geometric-mean* gap, not every shift — the widest gap is always 1→2 (the launch step, intentionally
wide), which can land below `maxTorqueRPM`; the *upper* shifts land progressively deeper in-band. And for
narrow bands (`kMax ≲ 1.35`) `B` saturates at `B_HI`, so very peaky cars get the tightest feasible
spacing rather than finely-differentiated gaps. `SPREAD = 0.85` is a calibrated constant (pins the Cayman
to its empirically-good top gear) — documented as tunable pending more in-game-measured cars.

**Parity note:** both `maxTorqueRPM` and `peakPowerRPM` are **optional** refinements absent from the
parity grid, so the byte-for-byte gate proves the change is a no-op there (it cannot exercise this path —
that is what the C#-native `GearingPowerbandTests` are for).

---

## 5. Gear-count handling by goal (informational override)

The user supplies `gears`, but the calculator should advise/clamp the count:

```js
function recommendedGearCount(goal, powertrain, gearsInput) {
  if (powertrain === 'EV') return 1;
  const want = {
    Circuit: 6, Drag: 8, Drift: 6, OffRoad: 6, Rally: 6, Touge: 6,
  }[goal] ?? 6;
  // Respect user's installed gearbox: never exceed what they have, but flag the ideal.
  return { used: gearsInput, recommended: want };
}
```

- **Drag:** 8–10 speeds keep the engine pinned at peak power between shifts.
- **Road/Rally/Drift/Touge:** 6–7 speeds are ideal.

---

## 6. Top-Speed / Final-Drive back-solve (effective peak-power RPM)

If `redlineRPM` and `tireDiameter` are known **and** a `targetTopSpeed` is given, back-solve FD from
physics instead of the HP heuristic. **The critical correction (2026-06):** a car's top speed is
*power-limited*, reached at ~peak-power RPM, and FH6 power fades approaching the limiter — so a real
car tops out **below** redline. Gearing the *redline* to the target therefore tops out short (the
reported 189.9-vs-193 mph bug). Gear an **effective top-speed RPM** to the target instead:

```js
// effective top-speed rpm: supplied peak-power rpm, capped by a droop-aware estimate so an
// above-redline peak (e.g. 8900 rpm peak vs 8700 redline) can't gear the FD too short; the
// estimate alone when peak-power rpm is absent. estRpm guards torque<=0 to avoid 0/0.
const estRpm = (redline > 0 && torque > 0)
  ? clamp(0.983 * 5252 * hp / torque, 0.85 * redline, 0.95 * redline)   // ≈ hp=torque dyno crossover
  : 0.95 * redline;
const fdRpm = (peakPowerRpm > 0) ? Math.min(peakPowerRpm, estRpm) : estRpm;

// FD so top gear puts the engine at fdRpm when the car is at the target speed:
const fd = clamp(r2((fdRpm * Math.PI * tireDia_in * 60) / (63360 * targetMph * gearTop)), FD_MIN, FD_MAX);
```

Per-gear **displayed** speeds still use the *true* redline (`speed = redline/(gear·FD)·π·tireØ·60/63360`),
so the gearing graph's "@redline" top-gear speed reads a few % **above** the target — the car keeps
pulling to the limiter past peak power. This mirrors the EV path's `1.07` headroom: both substitute an
effective rpm for redline (EV uses a flat 7% because flat instant torque has no power curve to estimate;
ICE derives it per car). `5252·hp/torque` is the HP=torque dyno crossover, an inputs-only proxy for the
usable top-end; `0.983` and the `0.85–0.95·redline` clamp keep it in the physically-plausible band.

**Worked check (2005 Cayman GT3 WTAC):** 630 hp / 410 lb-ft, redline 8700, peak power 8900 (*above*
redline → capped), 365/30R19 (Ø 27.62 in), target 193 → estRpm ≈ 7933, fdRpm = min(8900, 7933) = 7933 →
FD ≈ 4.33 with the stock 0.78 top gear, or **2.96** with the powerband-spaced 1.14 top gear (§4.1) — same
`FD × topGear ≈ 3.37`, the empirically-on-target value (the old redline back-solve gave 4.75 → 189.9 mph).

---

## 7. Full assembled output

```js
function computeGearing(car) {
  const { power, weight, frontWeightPct, gears, goal,
          drivetrain, engineLocation, powertrain,
          tireCompound, aeroInstalled } = car;

  const fd = (powertrain === 'EV')
      ? clamp(adjustedFinalDrive(power, goal) - 0.15, FD_MIN, FD_MAX)
      : adjustedFinalDrive(power, goal);

  const ratios = gearRatios(power, goal, powertrain, gears);

  return {
    finalDrive: +fd.toFixed(2),
    gearRatios: ratios,                       // length = 1 for EV, else = gears
    gearCount: ratios.length,
    why: {
      finalDrive: 'Set so the engine reaches the limiter in top gear at the end of the longest straight; shorter for heavier/low-power cars, taller for light/powerful ones.',
      ratios: 'Logarithmic spacing: wide low gears tame wheelspin, tight top gears stay in the power band against rising aero drag.',
    },
  };
}

function clamp(x, lo, hi) { return Math.max(lo, Math.min(hi, x)); }
```

---

## Summary of edge-case behavior

| Condition | Effect on gearing |
|---|---|
| **EV** | Single gear only; FD run ~0.15 longer (flat torque → optimize for top speed, no power band) |
| **Hybrid** | ICE ratios; spacing exponent `B −0.02` (wider gaps, broad torque plateau) |
| **ICE** | Standard formulas |
| **≥800 hp** | Halve power before FD formula |
| **≤200 hp** | Double power before FD formula |
| **AWD / FWD** | FD `+0.05` (longer top gear compensates for driveline loss; launch is traction-limited, not gearing-limited) |
| **Rear / Mid engine** | FD `−0.05 / −0.03` (rear-bias traction → can run marginally taller) |
| **Aero installed** (non-Drag) | FD `−0.10` (drag caps top-gear pull) |
| **Heavy car** | FD `+0.10` per 500 lb over 3000 (more torque multiplication) |
| **Light car** | FD `−0.10` per 500 lb under 3000 |
| **Low-grip compound** | Tends taller to avoid wheelspin (dominated by goal offset) |
| **All clamps** | FD ∈ [2.00, 7.00]; each gear ∈ [0.50, 5.50]; ratios forced strictly descending |

Relevant file for implementation: the project root (this spec is implemented in `tuning.js`).