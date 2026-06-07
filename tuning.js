/* =====================================================================
   FH6 Tuning Engine  (research-grounded)
   ---------------------------------------------------------------------
   compute(input, goal) -> structured tune object. Pure & deterministic.
   All math is IMPERIAL (lb, in, lb/in, hp, lb-ft); the UI converts
   metric input -> imperial before calling and back for display.

   Formulas are sourced from the community Forza tuning canon (consistent
   across FH4/FH5/FH6) and cross-referenced across ForzaFire, forza.guide,
   ForzaTune, grindout, skycoach et al., then reconciled by an adversarial
   review pass. Key formulas:
     • Springs   : ride-frequency model   K = f²·Wcorner / 9.78
     • Gearing   : FD = 4.25 + (400−hp)/600 ;  Rn = A·n^B  (B≈−0.65)
     • ARB       : (weight/2) / (200 − 200·stiffness%) + drivetrain split
     • Damping   : Bump = MinBump + frontAxle/200·0.1 ; Reb = Bump/0.6
     • Alignment : camber from tire-grip factor ; caster from weight+class
     • Aero      : level(goal) × balance(drivetrain) of each wing's range
     • Diff      : per-axle base + power-trim + per-goal adj ; AWD center%
   Two optional dials post-process the finished tune (skipped at 0, so the
   baseline is returned byte-for-byte): Handling Bias shifts front/rear BALANCE
   (a ratio knob); Overall Stiffness scales suspension FIRMNESS up/down the same
   direction at both ends (a magnitude knob) — the two are orthogonal.
   Every output is clamped to the legal Forza slider range.
   ===================================================================== */
(function () {
  "use strict";

  const GOALS = ["Circuit", "Drag", "Drift", "OffRoad", "Rally", "Touge"];
  const GOAL_META = {
    Circuit: { label: "Circuit", icon: "🏁", blurb: "Grip & balance on tarmac" },
    Drag:    { label: "Drag",    icon: "🚦", blurb: "Straight-line launch & top speed" },
    Drift:   { label: "Drift",   icon: "💨", blurb: "Controllable rear break-away" },
    OffRoad: { label: "Off-Road",icon: "⛰️", blurb: "Soft, tall, compliant" },
    Rally:   { label: "Rally",   icon: "🌲", blurb: "Mixed-surface compromise" },
    Touge:   { label: "Touge",   icon: "🏔️", blurb: "Tight technical mountain runs" },
  };
  const goalName = (g) => (GOAL_META[g] ? GOAL_META[g].label : g);

  /* ---------- helpers ---------- */
  const clamp = (x, lo, hi) => Math.min(hi, Math.max(lo, x));
  const r1 = (x) => Math.round(x * 10) / 10;
  const r2 = (x) => Math.round(x * 100) / 100;
  const rHalf = (x) => Math.round(x * 2) / 2;
  const r5 = (x) => Math.round(x / 5) * 5;
  const rInt = (x) => Math.round(x);
  const rEven = (x) => Math.round(x / 2) * 2;

  const PI_INDEX = { D: 0, C: 1, B: 2, A: 3, S1: 4, S2: 5, R: 6, X: 7 };

  /* =====================================================================
     DERIVED QUANTITIES (computed once per call)
     ===================================================================== */
  function derive(i) {
    const frac = clamp(i.frontWeightPct / 100, 0.2, 0.8);
    const rearFrac = 1 - frac;
    const frontAxle = i.weight * frac;
    const rearAxle = i.weight * rearFrac;
    const piIdx = PI_INDEX[i.piClass] != null ? PI_INDEX[i.piClass] : 3;
    const classTier = piIdx <= 1 ? "Sports" : piIdx <= 3 ? "HighPerf" : "Race";
    // tire grip factor: more grip tolerates/wants more negative camber, more diff lock, etc.
    const gripFactor = { Race: 1.0, Drag: 0.9, Sport: 0.8, Rally: 0.6, Street: 0.55, Stock: 0.5, Offroad: 0.45 }[i.tireCompound] || 0.7;
    const pw = i.power / i.weight;
    // Handling-tendency flags, shared by aero/arb/differential so the whole tune
    // compensates in one direction. A car is oversteer-prone when its mass sits
    // rearward — a rear engine (pendulum) or rear weight bias — or it's a powerful/
    // rear-light RWD. FWD is never oversteer-prone (it's the understeer case).
    const understeerProne = i.drivetrain === "FWD" || i.frontWeightPct >= 53;
    const oversteerProne = i.drivetrain !== "FWD" &&
      (i.engineLocation === "Rear" || i.frontWeightPct <= 46 ||
        (i.drivetrain === "RWD" && (i.frontWeightPct <= 49 || pw >= 0.16)));
    return {
      frac, rearFrac, frontAxle, rearAxle,
      frontCorner: frontAxle / 2, rearCorner: rearAxle / 2,
      pw,
      piIdx, classTier, gripFactor,
      understeerProne, oversteerProne,
      canTuneSusp: i.suspensionType !== "Stock",
      evFactor: i.powertrain === "EV" ? 1 : i.powertrain === "Hybrid" ? 0.5 : 0,
      isEV: i.powertrain === "EV",
    };
  }

  /* =====================================================================
     TIRES — front & rear cold psi   (range 15–55)
     ===================================================================== */
  function tires(i, d, goal) {
    const BASE = { Street: 29.0, Sport: 29.5, Race: 30.0, Rally: 27.0, Drag: 30.0, Offroad: 24.0, Stock: 28.5 };
    let base = BASE[i.tireCompound] != null ? BASE[i.tireCompound] : 29.0;
    const wAdj = clamp((i.weight - 3000) / 1323 * 1.0, -5, 5); // +1 psi / 600 kg
    base += wAdj;
    if (d.piIdx >= PI_INDEX.A) base += 0.5;                    // high-perf class bonus

    let f = base, r = base;
    if (i.aeroInstalled) { f += 0.5; r += 0.5; }
    if (d.piIdx >= PI_INDEX.S1) { f += 0.5; r += 0.5; }        // wide sticky tire proxy

    // drivetrain split (front does the steering / often driving). Engine
    // location intentionally does NOT bias tire pressure: the community
    // references set the F/R split by drivetrain only, and a mid/rear-engine
    // nudge over-widened the spread (e.g. AWD rear-engine to ~1.5 psi when AWD
    // guidance is ~0.2–0.5) and pointed the wrong way — a rear-heavy car's
    // loaded axle wants equal-or-more psi, not less. (See spec §1.6.)
    const split = { FWD: 1.5, RWD: 0.75, AWD: 0.35 }[i.drivetrain] || 0.5;
    f += split / 2; r -= split / 2;

    // per-goal
    if (goal === "Drag") {
      if (i.drivetrain === "FWD") { f = base - 8; r = base + 5; }
      else { f = base + 5; r = base - 8; }
    } else if (goal === "Drift") { f = 30; r = 27; }
    else if (goal === "OffRoad") { f -= 2; r -= 2; }
    else if (goal === "Rally") { f -= 1; r -= 1; }
    else if (goal === "Touge") { f += 0.5; r += 0.5; }

    f = clamp(rHalf(f), 15, 55); r = clamp(rHalf(r), 15, 55);
    return {
      front: f, rear: r,
      why: {
        text: `Cold pressures target the grip window for ${i.tireCompound} tires (~${BASE[i.tireCompound] || 29} psi baseline), ${wAdj >= 0 ? "raised" : "lowered"} ${Math.abs(r1(wAdj))} for the ${rInt(i.weight)} lb mass. ${i.drivetrain} biases the front +${r1(split)} psi over rear for steering response.` +
          (goal === "Drag" ? ` Drag floods the driven axle with grip (big soft patch) and firms the free axle to cut rolling drag.` : "") +
          (goal === "Drift" ? ` Drift runs low overall (30/27) so the rear slides predictably.` : "") +
          (goal === "OffRoad" || goal === "Rally" ? ` Lowered further for a bigger, more compliant patch on loose ground.` : ""),
        formula: `psi = compoundBase + (weight−3000)/1323 + classBonus ± split/2\n${goal} adjust; clamp 15–55, step 0.5`,
      },
    };
  }

  /* =====================================================================
     GEARING — final drive + ratios   FD∈[2,7], gear∈[0.5,5.5]
     ===================================================================== */
  function gearing(i, d, goal) {
    const FD_MIN = 2.0, FD_MAX = 7.0, GEAR_MIN = 0.5, GEAR_MAX = 5.5;
    const GOAL_G = {
      Circuit: { fd: 0.0, B: -0.65 }, Drag: { fd: -0.20, B: -0.58 }, Drift: { fd: 0.10, B: -0.70 },
      OffRoad: { fd: 0.75, B: -0.72 }, Rally: { fd: 0.50, B: -0.70 }, Touge: { fd: 0.20, B: -0.66 },
    }[goal];

    // continuous FD (critique fix: no halve/double cliff) anchored 400hp -> 4.25
    let fd = 4.25 + clamp((400 - i.power) / 600, -0.60, 0.60);
    fd += clamp((i.weight - 3000) / 500 * 0.10, -0.50, 0.50); // heavier -> shorter
    fd += GOAL_G.fd;
    if (i.aeroInstalled && goal !== "Drag") fd -= 0.10;       // drag caps top-gear pull
    // AWD/FWD: a marginally LONGER top gear (fd += 0.05) compensates for driveline
    // loss — the extra top-end keeps terminal speed where wheelspin/launch traction
    // already limits how much shorter gearing helps. (See spec-gearing §3, clarified.)
    if (i.drivetrain === "AWD" || i.drivetrain === "FWD") fd += 0.05;
    if (i.engineLocation === "Rear") fd -= 0.05;
    if (i.engineLocation === "Mid") fd -= 0.03;
    if (d.isEV) fd -= 0.15;                                   // flat torque -> longer single ratio
    fd = clamp(r2(fd), FD_MIN, FD_MAX);

    // EV single-speed: one TALL ratio (critique #8), only FD really matters.
    // Forza EVs are single-speed regardless of the gear-count field. The lone
    // ratio behaves like a TOP gear, so it sits in the ~0.9–1.6 region (not a
    // launch ratio) and scales with power-to-weight: a stronger car/pw can pull
    // a taller (numerically lower) single ratio, a weaker one wants it shorter.
    if (d.isEV) {
      const evRatio = r2(clamp(1.20 + (1 - d.pw / 0.40) * 0.30, 0.90, 1.60));
      const RL = i.redlineRpm, TD = i.tireDiameter, TT = i.targetTopSpeed;
      const canSpeed = RL > 0 && TD > 0;
      let fdSource = "heuristic";
      if (canSpeed && TT > 0) { fd = clamp(r2((RL * Math.PI * TD * 60) / (63360 * TT * evRatio)), FD_MIN, FD_MAX); fdSource = "target"; }
      const speeds = canSpeed ? [RL / (evRatio * fd) * Math.PI * TD * 60 / 63360] : null;
      return {
        final: fd, ratios: [evRatio], singleSpeed: true, speeds, topSpeed: speeds ? speeds[0] : null, fdSource,
        why: {
          text: (fdSource === "target"
              ? `This EV is single-speed; the final drive is back-solved from physics so it reaches your target top speed at the ${RL} rpm redline — exact, not an estimate. The lone ratio (${evRatio}) is sized to this car's ${r2(d.pw)} hp/lb. `
              : `This EV is single-speed, so only the final drive (${fd}) and the lone ratio set the speed/accel trade-off; that ratio (${evRatio}) is tuned to this car's ${r2(d.pw)} hp/lb (taller for stronger cars) and the final drive runs slightly long because flat instant torque has no power band to keep. `) +
            (canSpeed ? `Top speed is computed from your redline and tire diameter.` : ``),
          formula: (fdSource === "target"
              ? `evRatio = clamp(1.20 + (1 − pw/0.40)×0.30, 0.90, 1.60)\nFD = redline × π × tireØ × 60 / (63360 × targetMph × evRatio)`
              : `evRatio = clamp(1.20 + (1 − pw/0.40)×0.30, 0.90, 1.60)\nFD = 4.25 + clamp((400−hp)/600, ±0.6) + weight&goal adj − 0.15 (EV)`),
        },
      };
    }

    // first gear from power-to-weight (more pw → taller 1st), nudged by goal
    let A = 3.40 - clamp((d.pw - 0.05) / 0.35, 0, 1) * 1.0;   // 3.40 → 2.40
    A += { Circuit: 0, Drag: 0.30, Drift: -0.10, OffRoad: 0.40, Rally: 0.30, Touge: 0.10 }[goal] || 0;
    A = clamp(A, 1.80, 5.50);
    let B = GOAL_G.B;
    if (i.powertrain === "Hybrid") B -= 0.02;
    if (i.drivetrain === "AWD" && (goal === "Rally" || goal === "OffRoad")) B -= 0.02;

    const N = clamp(Math.round(i.gears), 2, 10);
    let ratios = [];
    for (let n = 1; n <= N; n++) ratios.push(clamp(A * Math.pow(n, B), GEAR_MIN, GEAR_MAX));
    // enforce strictly descending (critique #7)
    for (let k = 1; k < ratios.length; k++) if (ratios[k] >= ratios[k - 1] - 0.01) ratios[k] = ratios[k - 1] - 0.05;
    const lo = Math.min.apply(null, ratios);
    if (lo < GEAR_MIN) { const add = GEAR_MIN - lo; ratios = ratios.map((x) => x + add); } // lift whole set if floored
    ratios = ratios.map((x) => r2(clamp(x, GEAR_MIN, GEAR_MAX)));

    // optional physics: real top speed / per-gear speeds, and target-based FD back-solve
    const RL = i.redlineRpm, TD = i.tireDiameter, TT = i.targetTopSpeed;
    const canSpeed = RL > 0 && TD > 0;
    const topRatio = ratios[ratios.length - 1];
    let fdSource = "heuristic";
    if (canSpeed && TT > 0 && topRatio > 0) {
      fd = clamp(r2((RL * Math.PI * TD * 60) / (63360 * TT * topRatio)), FD_MIN, FD_MAX);
      fdSource = "target";
    }
    const speeds = canSpeed ? ratios.map((gr) => RL / (gr * fd) * Math.PI * TD * 60 / 63360) : null;
    const topSpeed = speeds ? speeds[speeds.length - 1] : null;

    return {
      final: fd, ratios, singleSpeed: false, speeds, topSpeed, fdSource,
      why: {
        text: (fdSource === "target"
            ? `Final drive is back-solved from physics so top gear (${r2(topRatio)}) just reaches your target top speed at the ${RL} rpm redline — more exact than the power heuristic. `
            : `Final drive uses the community formula anchored at 400 hp → 4.25, shifted for this car's ${i.power} hp and ${rInt(i.weight)} lb, then ${GOAL_G.fd >= 0 ? "+" : ""}${GOAL_G.fd} for ${goalName(goal)}. `) +
          `Gears follow Rₙ = A·nᴮ with 1st = ${r2(A)} (from ${r2(d.pw)} hp/lb) and spacing exponent B = ${B} — wide low gears tame wheelspin, tight top gears stay in the power band.` +
          (canSpeed ? ` Per-gear and top speeds are computed from your ${RL} rpm redline and tire diameter.` : ``),
        formula: (fdSource === "target"
            ? `FD = redline × π × tireØ × 60 / (63360 × targetMph × topGear)`
            : `FD = 4.25 + clamp((400−hp)/600, ±0.6) + weightAdj + goalAdj`) +
          `\nRₙ = ${r2(A)} × n^${B}` + (canSpeed ? `\nspeed = redline / (gear × FD) × π × tireØ × 60/63360` : ``),
      },
    };
  }

  /* =====================================================================
     ALIGNMENT — camber, toe, caster
     ===================================================================== */
  function alignment(i, d, goal) {
    if (!d.canTuneSusp) {
      return { camberF: 0, camberR: 0, toeF: 0, toeR: 0, caster: 5.0,
        why: { text: "Stock suspension locks alignment at the factory setting — install an upgraded suspension to tune camber, toe and caster.", formula: "stock = locked" } };
    }
    const g = d.gripFactor;
    // front camber from grip, drivetrain, front load
    let camF = -(0.6 + (g - 0.45) / 0.55 * (2.0 - 0.6));
    camF += { RWD: -0.3, AWD: 0.0, FWD: 0.3 }[i.drivetrain] || 0;
    const effFw = i.frontWeightPct + (i.engineLocation === "Rear" ? -3 : i.engineLocation === "Mid" ? -1.5 : 0);
    camF += -0.1 * (effFw - 50) / 4;
    camF = {
      Circuit: (c) => c - 0.2, Drag: () => -0.3,
      Drift: () => clamp(-3.0 - g * 2.0, -5.0, -3.0), OffRoad: () => -0.5,
      Rally: () => clamp(-0.8 - g * 0.4, -1.2, -0.8), Touge: (c) => c + 0.1,
    }[goal](camF);
    camF = clamp(r1(camF), -5, 0);

    // rear camber ~ half of front
    let camR = camF * 0.55 + ({ RWD: 0.2, AWD: 0, FWD: -0.2 }[i.drivetrain] || 0) - 0.1 * (d.rearFrac * 100 - 50) / 6;
    camR = {
      Circuit: (r) => clamp(r, -1.0, -0.5), Drag: () => -0.2, Drift: () => -1.0,
      OffRoad: () => -0.5, Rally: () => clamp(-0.5 - g * 0.3, -0.8, -0.5), Touge: (r) => clamp(r + 0.05, -1.0, -0.4),
    }[goal](camR);
    camR = clamp(r1(camR), -5, 0);
    if (i.engineLocation !== "Front" && goal !== "OffRoad") camR = clamp(r1(camR - 0.2), -5, 0);

    // front toe (− = toe-out)
    const understeerProne = i.drivetrain === "FWD" || effFw >= 55;
    // Circuit −0.05 (critique #9: differentiate vs Drag's flat 0.0); OffRoad −0.2
    // (critique #9: loose-surface toe-out for turn-in, and keeps Circuit/Drag/OffRoad
    // meaningfully distinct even for non-understeer-prone RWD/<55% cars).
    let toeF = {
      Circuit: () => (understeerProne ? -0.1 : -0.05), Drag: () => 0.0, Drift: () => -0.2,
      OffRoad: () => -0.2, Rally: () => -0.1, Touge: () => -0.1,
    }[goal]();
    toeF = clamp(r1(toeF), -5, 5);

    // rear toe (+ = toe-in)
    let toeR = {
      Circuit: () => (i.drivetrain === "RWD" ? 0.1 : 0.0), Drag: () => 0.1, Drift: () => -0.1,
      OffRoad: () => 0.1, Rally: () => 0.1, Touge: () => (i.drivetrain === "RWD" ? 0.2 : 0.1),
    }[goal]();
    if (i.drivetrain === "RWD" && i.torque >= 400 && goal !== "Drift") toeR += 0.1;
    toeR = clamp(r1(toeR), -5, 5);

    // caster from weight + class + aero
    let caster = 5.0 + clamp((i.weight - 2400) / 1800, 0, 1) * 2.0;
    caster += { D: -0.3, C: -0.1, B: 0, A: 0.1, S1: 0.3, S2: 0.4, R: 0.45, X: 0.5 }[i.piClass] || 0;
    if (i.aeroInstalled) caster += 0.2;
    caster = { Circuit: (c) => c, Drag: () => 5.0, Drift: (c) => c + 1.0, OffRoad: (c) => c - 0.5, Rally: (c) => c - 0.3, Touge: (c) => c + 0.2 }[goal](caster);
    caster = clamp(r1(caster), 1, 7);
    if (goal === "Circuit" || goal === "Touge" || goal === "Drift") caster = clamp(caster, 5, 7);

    return {
      camberF: camF, camberR: camR, toeF, toeR, caster,
      why: {
        text: `Front camber comes from the ${i.tireCompound} grip factor (${g}), ${i.drivetrain} bias and front load → ${camF}°; rear runs ~55% of that (${camR}°). ${goalName(goal)} ${goal === "Drift" ? "pushes front camber to the limit for big steering angles and adds front toe-out for counter-steer" : "keeps toe near zero to avoid scrub"}. Caster ${caster}° scales with the ${rInt(i.weight)} lb mass and ${i.piClass}-class speed for self-centring.`,
        formula: `camberF = −(0.6 + (grip−0.45)/0.55 × 1.4) + dtBias + loadTrim → ${goalName(goal)}\ncaster = 5.0 + clamp((wt−2400)/1800)×2 + classBump`,
      },
    };
  }

  /* =====================================================================
     ANTI-ROLL BARS   (range 1–65)
     ===================================================================== */
  function arb(i, d, goal) {
    if (!d.canTuneSusp) return { front: 32.5, rear: 32.5, why: { text: "Stock suspension can't meaningfully tune anti-roll bars — values centred. Upgrade the suspension to balance roll stiffness.", formula: "stock = centred" } };
    const stiffPct = { D: 0.63, C: 0.63, B: 0.55, A: 0.50, S1: 0.45, S2: 0.42, R: 0.41, X: 0.40 }[i.piClass] || 0.5;
    const base = (i.weight / 2) / (200 - 200 * stiffPct);     // ForzaFire base
    const splitPer1 = { RWD: 1.0, AWD: 0.66, FWD: -1.0 }[i.drivetrain];
    const splitDelta = splitPer1 * (i.frontWeightPct - 50);
    let front = base + splitDelta / 2;
    let rear = base - splitDelta / 2;

    const gm = { Circuit: [1, 1], Drag: [0.40, 0.55], Drift: [0.45, 1.45], OffRoad: [0.30, 0.30], Rally: [0.55, 0.55], Touge: [1.05, 0.95] }[goal];
    front *= gm[0]; rear *= gm[1];

    if (i.drivetrain === "FWD" && goal !== "Drift") front = Math.min(front, base * 0.85);
    if (i.drivetrain === "AWD" && (goal === "Circuit" || goal === "Touge")) { front *= 0.92; rear *= 1.08; }
    if (i.engineLocation !== "Front" && goal !== "Drift") rear *= 0.92;
    if (i.aeroInstalled && (goal === "Circuit" || goal === "Touge")) { front *= 1.08; rear *= 1.08; }
    if (d.isEV) { front *= 1.05; rear *= 1.05; }
    // Oversteer-prone car: shift roll stiffness forward (firmer front bar, softer rear)
    // so the loose rear axle keeps its grip. Drift wants the opposite, so leave it be.
    if (d.oversteerProne && goal !== "Drift") { front *= 1.06; rear *= 0.80; }

    front = clamp(r2(front), 1, 65); rear = clamp(r2(rear), 1, 65);
    return {
      front, rear,
      why: {
        text: `Base bar from the ForzaFire formula: (½ weight) ÷ (200 − 200 × ${stiffPct} class-stiffness) = ${r1(base)}, split ${i.drivetrain === "FWD" ? "softer front" : "stiffer front"} by the ${r1(i.frontWeightPct)}% weight bias. ${goalName(goal)} then scales front ×${gm[0]} / rear ×${gm[1]}` +
          (goal === "Drift" ? ` — a soft front + very stiff rear provokes rotation on demand.` : goal === "OffRoad" ? ` — very soft both ends so wheels stay loaded over terrain.` : `.`),
        formula: `base = (weight/2) / (200 − 200·stiff%)\nfront = base + split/2 ;  rear = base − split/2\n× ${goalName(goal)} [${gm[0]}, ${gm[1]}] ; clamp 1–65`,
      },
    };
  }

  /* =====================================================================
     SPRINGS + RIDE HEIGHT   (ride-frequency model)
     ===================================================================== */
  function springs(i, d, goal) {
    // Per-axle part ranges: front and rear sliders can have independent ranges
    // in-game. Fall back to the legacy shared key if an axle-specific value is
    // absent (keeps older saved inputs / callers working).
    const srMinF = i.springRateMinF != null ? i.springRateMinF : i.springRateMin;
    const srMaxF = i.springRateMaxF != null ? i.springRateMaxF : i.springRateMax;
    const srMinR = i.springRateMinR != null ? i.springRateMinR : i.springRateMin;
    const srMaxR = i.springRateMaxR != null ? i.springRateMaxR : i.springRateMax;
    const rhMinF = i.rideHeightMinF != null ? i.rideHeightMinF : i.rideHeightMin;
    const rhMaxF = i.rideHeightMaxF != null ? i.rideHeightMaxF : i.rideHeightMax;
    const rhMinR = i.rideHeightMinR != null ? i.rideHeightMinR : i.rideHeightMin;
    const rhMaxR = i.rideHeightMaxR != null ? i.rideHeightMaxR : i.rideHeightMax;
    const FREQ_BASE = { Sports: 1.9, HighPerf: 2.2, Race: 2.5 }[d.classTier];
    const SUSP_CAP = { Stock: 1.6, Street: 2.2, Sport: 2.8, Race: 5.0, Drift: 3.2, Offroad: 2.0 }[i.suspensionType] || 5.0;

    let fFront = FREQ_BASE, fRear = FREQ_BASE;
    if (i.aeroInstalled) { fFront += 0.6; fRear += 0.6; }

    const G = { Circuit: { fOff: [0.0, -0.1], mul: [1, 1] }, Drag: { fOff: [-0.6, 0.2], mul: [1, 1] }, Drift: { fOff: [-0.3, 0.4], mul: [1, 1] }, OffRoad: { override: [1.1, 1.0] }, Rally: { fOff: [0, 0], mul: [0.5, 0.5] }, Touge: { fOff: [-0.1, -0.2], mul: [1, 1] } }[goal];
    if (G.override) { fFront = G.override[0]; fRear = G.override[1]; }
    else { fFront = fFront * G.mul[0] + G.fOff[0]; fRear = fRear * G.mul[1] + G.fOff[1]; }
    fFront = clamp(fFront, 0.8, SUSP_CAP); fRear = clamp(fRear, 0.8, SUSP_CAP);

    // drivetrain split
    const sp = { FWD: [-0.10, 0.10], AWD: [0.05, 0.05], RWD: [0.05, -0.05] }[i.drivetrain] || [0, 0];
    fFront = clamp(fFront + sp[0], 0.8, SUSP_CAP); fRear = clamp(fRear + sp[1], 0.8, SUSP_CAP);

    // K = f²·Wcorner / 9.78   (critique-corrected coefficient)
    let front = (fFront * fFront * d.frontCorner) / 9.78;
    let rear = (fRear * fRear * d.rearCorner) / 9.78;
    const devPts = (i.frontWeightPct - 50);
    const dirn = i.drivetrain === "FWD" ? -1 : 1;
    front += 15 * Math.max(0, devPts) * dirn * 0.5;
    rear -= 15 * Math.max(0, devPts) * dirn * 0.5;
    if (i.powertrain === "EV") { front *= 1.08; rear *= 1.08; }
    if (i.powertrain === "Hybrid") { front *= 1.04; rear *= 1.04; }

    front = clamp(front, srMinF, srMaxF); rear = clamp(rear, srMinR, srMaxR);
    // Physics floor: springs must support the car without bottoming. If the goal's
    // soft frequency target lands too soft to hold the static load, raise BOTH just
    // enough (proportional) — not all the way to max, so soft goals stay soft.
    let rangeNote = "";
    const support = front * 2 * rhMinF + rear * 2 * rhMinR;
    if (support < i.weight) {
      const scale = i.weight / support;
      front = clamp(front * scale, srMinF, srMaxF); rear = clamp(rear * scale, srMinR, srMaxR);
      rangeNote = " (raised to the support floor for this weight)";
    }
    front = rInt(front); rear = rInt(rear);

    // RIDE HEIGHT — fraction of each axle's own part range per goal + modifiers
    let [pF, pR] = { Circuit: [0, 0], Drag: [0, 0.30], Drift: [0.05, 0.05], OffRoad: [1, 1], Rally: [0.75, 0.75], Touge: [0.05, 0.05] }[goal];
    const rhComp = { Rally: 0.10, Offroad: 0.15 }[i.tireCompound] || 0;
    pF = clamp(pF + rhComp, 0, 1); pR = clamp(pR + rhComp, 0, 1);
    if (i.aeroInstalled && (goal === "Circuit" || goal === "Touge" || goal === "Drag")) pR = clamp(pR + 0.05, 0, 1);
    const softFront = (front - srMinF) < 0.25 * (srMaxF - srMinF);
    if (softFront && pF < 0.15) { pF = clamp(pF + 0.10, 0, 1); pR = clamp(pR + 0.10, 0, 1); }
    if (i.weight > 4000) { pF = clamp(pF + 0.05, 0, 1); pR = clamp(pR + 0.05, 0, 1); }
    const rideF = r1(clamp(rhMinF + (rhMaxF - rhMinF) * pF, rhMinF, rhMaxF));
    const rideR = r1(clamp(rhMinR + (rhMaxR - rhMinR) * pR, rhMinR, rhMaxR));

    return {
      front, rear, rideF, rideR, _fFront: fFront, _fRear: fRear,
      why: {
        text: `Spring rate is solved from a target ride frequency (${r1(fFront)} Hz front / ${r1(fRear)} Hz rear for ${d.classTier} class${i.aeroInstalled ? " + aero" : ""}, ${goalName(goal)}) against the ${rInt(d.frontCorner)} lb on each front corner: K = f²·W/9.78${rangeNote}. Ride height sits at ${Math.round(pF * 100)}% of travel — ${pF < 0.2 ? "as low as practical for a low CoG" : pF > 0.8 ? "high for clearance" : "raised for compliance"}${goal === "Drag" ? ", with forward rake (higher rear) to plant the drive wheels" : ""}.`,
        formula: `K = f² × Wcorner / 9.78   (f from class+aero+${goalName(goal)})\nride = rhMin + (rhMax−rhMin) × goalFrac`,
      },
    };
  }

  /* =====================================================================
     DAMPING — rebound & bump (1–20)
     ===================================================================== */
  function damping(i, d, goal, spr) {
    const MIN_BUMP = { Sports: 4.6, HighPerf: 4.7, Race: 4.8 }[d.classTier];
    let fBump = MIN_BUMP + (d.frontAxle / 200) * 0.1;
    let fReb = fBump / 0.6;

    const diffPct = Math.abs(spr.front - spr.rear) / Math.max(spr.front, spr.rear, 1) * 100;
    const off = diffPct <= 1.5 ? { reb: -0.2, bump: -0.1 } : diffPct <= 35 ? { reb: -0.3, bump: -0.2 } : diffPct <= 40 ? { reb: -0.6, bump: -0.4 } : { reb: -1.2, bump: -0.8 };
    let rBump = fBump + off.bump;
    let rReb = fReb + off.reb;

    const dg = { Circuit: [1, 1, 0], Drag: [1.10, 1.05, 0], Drift: [0.55, 0.55, 0], OffRoad: [0.30, 0.45, 1.0], Rally: [0.55, 0.65, 1.0], Touge: [0.85, 0.90, 0] }[goal];
    fBump *= dg[0]; rBump *= dg[0];
    fReb = fReb * dg[1] + dg[2]; rReb = rReb * dg[1] + dg[2];

    if (i.drivetrain === "FWD") { fBump += 0.5; fReb += 0.5; rBump -= 0.3; rReb -= 0.3; }
    else if (i.drivetrain === "RWD") { rBump += 0.3; rReb += 0.3; }
    else { fBump += 0.2; fReb += 0.2; rBump += 0.2; rReb += 0.2; }

    if (goal === "Drag" && i.drivetrain === "RWD") { fReb = Math.max(fReb - 2.0, 1.0); fBump += 1.0; rReb += 1.0; }
    if (i.engineLocation !== "Front") { rBump += 0.4; rReb += 0.4; }
    if (i.powertrain === "EV") { fReb += 0.3; rReb += 0.3; }
    if (goal === "OffRoad" || goal === "Rally" || i.suspensionType === "Offroad") { fBump = Math.max(1.0, fBump); rBump = Math.max(1.0, rBump); }

    fBump = clamp(fBump, 1, 20); rBump = clamp(rBump, 1, 20);
    fReb = clamp(fReb, 1, 20); rReb = clamp(rReb, 1, 20);
    // ratio guard (bump 40–70% of rebound) — bypass for intentionally-asymmetric goals (critique #6)
    const bypass = (goal === "Drag" && i.drivetrain === "RWD") || goal === "OffRoad" || goal === "Rally";
    if (!bypass) { fBump = clamp(fBump, 0.4 * fReb, 0.7 * fReb); rBump = clamp(rBump, 0.4 * rReb, 0.7 * rReb); }

    return {
      reboundF: r1(fReb), reboundR: r1(rReb), bumpF: r1(fBump), bumpR: r1(rBump),
      why: {
        text: `Bump is derived from the ${rInt(d.frontAxle)} lb front axle (MinBump ${MIN_BUMP} + axle/200×0.1) and rebound set at ~60% above it. ${goalName(goal)} scales bump ×${dg[0]} / rebound ×${dg[1]}${dg[2] ? ` +${dg[2]} rebound` : ""}` +
          (goal === "OffRoad" || goal === "Rally" ? ` for terrain compliance and recovery.` : ` for a ${dg[0] < 0.7 ? "compliant" : "stable"} platform.`) +
          ` ${i.drivetrain} biases damping toward the ${i.drivetrain === "FWD" ? "front" : "rear"}.`,
        formula: `bump = MinBump(class) + frontAxle/200 × 0.1\nrebound = bump / 0.6  →  × ${goalName(goal)} mults ; clamp 1–20`,
      },
    };
  }

  /* =====================================================================
     AERO — front & rear downforce as % of each wing's slider range
     ===================================================================== */
  function aero(i, d, goal) {
    const hasF = i.hasFrontAero, hasR = i.hasRearAero;
    if (!hasF && !hasR) {
      return { applicable: false, front: null, rear: null,
        why: { text: "No adjustable aero is installed, so there's nothing to set here — grip comes from the body itself.", formula: "—" } };
    }
    const LEVEL = { Circuit: 0.85, Touge: 0.55, Drift: 0.30, Rally: 0.15, OffRoad: 0.05, Drag: 0.0 }[goal];

    // optional downforce ranges (imperial lbf) -> map a slider fraction to actual lbf
    const fR = i.aeroFront || [null, null], rR = i.aeroRear || [null, null];
    const toLbf = (frac, rng) => (rng[0] != null && rng[1] != null) ? Math.round(rng[0] + (rng[1] - rng[0]) * clamp(frac, 0, 1)) : null;
    const hasRange = (rng) => rng[0] != null && rng[1] != null;
    const lbfNote = " The % is mapped into the downforce range you entered to give the lbf shown.";
    const lbfFormula = "\nlbf = min + (max − min) × fraction";

    // With only ONE wing you can't rebalance the car — adding downforce to the end you
    // have shifts balance that way at speed, so the present wing is sized to the car's
    // existing tendency rather than to a target front/rear share.
    const understeerProne = d.understeerProne, oversteerProne = d.oversteerProne;

    if (hasF && !hasR) {                          // front splitter only
      const fac = oversteerProne ? 0.6 : understeerProne ? 1.0 : 0.85;
      let f = goal === "Drag" ? 0 : clamp(LEVEL * fac, 0, 1);
      const fp = r5(f * 100);
      return { applicable: true, front: fp, frontLbf: toLbf(f, fR), rear: null, rearLbf: null,
        why: { text: `Front splitter only — there's no rear wing to balance against. ${goalName(goal)} runs ${fp}% of the splitter's range` +
            (goal === "Drag" ? `, floored — any front downforce is drag here.` : oversteerProne ? `, kept moderate: on a tail-happy ${i.drivetrain} car a big splitter lightens the rear at speed and invites high-speed oversteer you can't dial out with a rear wing.` : understeerProne ? `, used fully — front downforce directly fights this car's understeer.` : `.`) + (hasRange(fR) ? lbfNote : ``),
          formula: `front = level(${LEVEL}) × balanceFactor(${fac})\nrear = n/a (no wing installed)` + (hasRange(fR) ? lbfFormula : ``) } };
    }
    if (!hasF && hasR) {                          // rear wing only
      const fac = understeerProne ? 0.55 : oversteerProne ? 1.0 : 0.8;
      let r = goal === "Drag" ? 0 : clamp(LEVEL * fac, 0, 1);
      if (i.engineLocation === "Rear") r = clamp(r - 0.10, 0, 1);
      if (i.engineLocation === "Mid") r = clamp(r - 0.05, 0, 1);
      const rp = r5(r * 100);
      return { applicable: true, front: null, frontLbf: null, rear: rp, rearLbf: toLbf(r, rR),
        why: { text: `Rear wing only — there's no front splitter to balance against. ${goalName(goal)} runs ${rp}% of the wing's range` +
            (goal === "Drag" ? `, floored — the wing is pure drag on a straight.` : understeerProne ? `, kept low: this car already pushes and you can't add front downforce to offset it, so a big wing would only deepen high-speed understeer.` : oversteerProne ? `, used fully — rear downforce calms a loose ${i.drivetrain} rear at speed.` : `.`) + (hasRange(rR) ? lbfNote : ``),
          formula: `rear = level(${LEVEL}) × balanceFactor(${fac})\nfront = n/a (no splitter installed)` + (hasRange(rR) ? lbfFormula : ``) } };
    }

    // full kit (both ends) — solve for a target front/rear balance share
    let bal = { FWD: 0.50, RWD: 0.525, AWD: 0.425 }[i.drivetrain] || 0.5; // front share
    if (goal === "Drift") bal = clamp(bal - 0.025, 0.45, 0.5);

    const shift = (bal - 0.5) * 0.6;
    let front = clamp(LEVEL + shift, 0, 1);
    let rear = clamp(LEVEL - shift, 0, 1);
    // nose-heavy → more rear DF. Research §1.2 / findings: +1.87 lbf rear per 1%
    // front-weight above the 47% reference. Convert that absolute lbf to a slider
    // fraction against the actual rear span (fallback to a 250 lbf standard span).
    const rearSpan = (rR[0] != null && rR[1] != null && rR[1] > rR[0]) ? (rR[1] - rR[0]) : 250;
    rear = clamp(rear + (i.frontWeightPct - 47) * 1.87 / rearSpan, 0, 1);
    if (i.power >= 600 && goal !== "Drag" && goal !== "OffRoad") {
      const k = 1 + Math.min((i.power - 600) / 600, 0.5) * 0.5;
      front = clamp(front * k, 0, 1); rear = clamp(rear * k, 0, 1);
    }
    if (i.drivetrain === "AWD" && (goal === "Circuit" || goal === "Touge")) {
      // Default AWD: max front / min rear — the rear axle already has drive traction and
      // a big wing is mostly drag. But a tail-happy AWD (rear engine / rear weight bias)
      // needs the rear planted at speed, so flip to a big rear wing + a moderate front.
      if (oversteerProne) { front = 0.70; rear = 0.95; }
      else { front = 1.0; rear = 0.15; }
    }
    // Trim rear downforce for rear/mid engine ONLY when the car isn't already tail-happy.
    // An oversteer-prone car wants MORE rear wing, not less (this used to subtract it,
    // lightening the very axle that was floating away at speed).
    if (!oversteerProne) {
      if (i.engineLocation === "Rear") rear = clamp(rear - 0.10, 0, 1);
      if (i.engineLocation === "Mid") rear = clamp(rear - 0.05, 0, 1);
    }
    if (goal === "Drag") { front = 0; rear = 0; }

    const fp = r5(front * 100), rp = r5(rear * 100);
    const frontLbf = toLbf(front, fR), rearLbf = toLbf(rear, rR);
    // Balance from ACTUAL downforce when the lbf ranges are known — comparing "% of
    // range" across a small front splitter and a large rear wing is meaningless and
    // used to report a wildly front-biased share for a rear-biased car.
    const share = (frontLbf != null && rearLbf != null && frontLbf + rearLbf > 0)
      ? Math.round(frontLbf / (frontLbf + rearLbf) * 100)
      : (fp + rp > 0 ? Math.round(fp / (fp + rp) * 100) : 50);
    return {
      applicable: true, front: fp, frontLbf, rear: rp, rearLbf,
      why: {
        text: `Downforce trades top speed for grip. ${goalName(goal)} runs ${fp}% front / ${rp}% of each wing's range (aero balance ≈ ${share}% front)` +
          (goal === "Circuit" ? `, near-max for the most grip` + (i.drivetrain === "AWD" ? (oversteerProne ? `; this tail-happy AWD runs a big rear wing plus a moderate front to plant the rear at speed instead of letting it float away.` : `; AWD forces max front / min rear since the rear already has drive traction and a rear wing would only add drag.`) : `.`) : goal === "Drag" ? `, floored to zero — every pound of downforce is drag that kills top speed.` : goal === "Drift" ? `, low so the car stays loose and easy to swing.` : `, a moderate amount for the surface and speed.`) + ((hasRange(fR) || hasRange(rR)) ? lbfNote : ``),
        formula: `front = level(${LEVEL}) + balanceShift\nrear  = level − balanceShift + weightTrim   (% of wing range)` + ((hasRange(fR) || hasRange(rR)) ? lbfFormula : ``),
      },
    };
  }

  /* =====================================================================
     BRAKING — balance (% front) & pressure (%)
     ===================================================================== */
  function braking(i, d, goal) {
    let bias = { RWD: 52, AWD: 54, FWD: 58 }[i.drivetrain] || 53;
    bias += clamp((i.frontWeightPct - 50) * 0.5, -6, 6);
    const engBias = i.engineLocation === "Rear" ? -1.0 : i.engineLocation === "Mid" ? -0.5 : 0;
    bias += engBias;
    // EV regen by drive axle (critique #13)
    bias += i.powertrain === "EV" ? (i.drivetrain === "FWD" ? -1 : i.drivetrain === "AWD" ? 0.5 : 1) : i.powertrain === "Hybrid" ? 0.5 : 0;
    const lowGrip = i.tireCompound === "Rally" || i.tireCompound === "Offroad";
    // cap combined surface+goal rearward shift (critique #12)
    let rearShift = (lowGrip ? -3 : 0) + ({ Drag: 3, OffRoad: -4, Rally: -3, Touge: -1, Circuit: 0 }[goal] || 0);
    if (goal !== "Drift") { bias += Math.max(rearShift, -4 + (rearShift > 0 ? rearShift : 0)); }
    if (goal === "Drift") bias = 48;
    bias = rInt(clamp(bias, 40, 65));

    let pres = 100;
    pres += clamp((i.weight - 3000) / 1000 * 5, -10, 12);
    pres += { Race: 5, Sport: 2, Street: 0, Drag: 5, Rally: -5, Offroad: -8, Stock: 0 }[i.tireCompound] || 0;
    pres -= d.evFactor * 3;
    pres += { Circuit: 5, Drag: -10, Drift: -5, OffRoad: -10, Rally: -5, Touge: 3 }[goal] || 0;
    pres = r5(clamp(pres, 80, 130));

    return {
      balance: bias, pressure: pres,
      why: {
        text: `${bias}% front balance suits ${i.drivetrain} with ${r1(i.frontWeightPct)}% front weight — enough front brake to use forward weight transfer without locking the rear. ` +
          (goal === "Drift" ? `Drift overrides to 48% (rear-biased) so braking helps rotate the car. ` : "") +
          `Pressure ${pres}% is tuned so tires lock only in the last sliver of trigger pull on ${i.tireCompound} tires` + (goal === "OffRoad" || goal === "Rally" ? `, eased for the loose surface.` : `.`),
        formula: `balance = dtBase + (frontPct−50)×0.5 + goal  (clamp 40–65)\npressure = 100 + massAdj + compound + goal  (clamp 80–130, step 5)`,
      },
    };
  }

  /* =====================================================================
     DIFFERENTIAL — accel/decel lock %, per axle (+ AWD center)
     ===================================================================== */
  function differential(i, d, goal) {
    // power trim: high power loosens accel, low power tightens
    const trim = clamp(-((d.pw - 0.13) / 0.05) * 6, -16, 10);
    const ptAccel = i.powertrain === "EV" ? -6 : i.powertrain === "Hybrid" ? -3 : 0;
    const lowGrip = i.tireCompound === "Rally" || i.tireCompound === "Offroad";

    const DG = {
      Circuit: { rwd: [0, 0], fwd: [0, 0], awd: [0, 0, 0, 0, null, 0] },
      Drag: { rwd: [null, null, 90, 0], fwd: [null, null, 90], awd: [10, 0, 20, 0, 85] },
      Drift: { rwd: [null, null, 100, 30], fwd: [null, null, 90], awd: [20, 10, 30, 20, 88] },
      OffRoad: { rwd: [24, null, null, 20], fwd: [20, 7], awd: [10, 8, -10, -8, 60] },
      Rally: { rwd: [12, 5], fwd: [15, 7], awd: [5, 5, -15, -8, 70] },
      Touge: { rwd: [18, -2], fwd: [10, 0], awd: [-10, 0, -5, -10, 80] },
    }[goal];

    if (i.drivetrain === "FWD") {
      const f = DG.fwd; // [accelAdj, decelAdj, accelOverride?]
      let accel = f[2] != null ? f[2] : 60 + trim + f[0] + ptAccel;
      let decel = 8 + (f[1] || 0);
      if (lowGrip) accel -= 6;
      accel = rEven(clamp(accel, 0, 95)); decel = rInt(clamp(decel, 5, 100));
      return { driveline: "FWD", accel, decel, why: diffWhy("FWD", goal, accel, decel, i) };
    }
    if (i.drivetrain === "AWD") {
      const a = DG.awd; // [fAccelAdj,fDecelAdj,rAccelAdj,rDecelAdj,centerOverride]
      let fA = 30 + trim * 0.5 + a[0] + ptAccel * 0.5;
      let fD = 5 + a[1];
      let rA = 80 + trim + a[2] + ptAccel;
      let rD = 30 + a[3];
      let center = a[4] != null ? a[4] : 78;
      center += clamp((i.frontWeightPct - 50) * 0.4, -6, 8);
      if (i.power >= 600) center += 3;
      if (i.engineLocation !== "Front") rA -= 4;
      if (lowGrip) { rA -= 6; fA -= 6; }
      // Tail-happy AWD: send less torque rearward and lighten the rear locks so it stops
      // rotating like a sharpened RWD on power and lift-off. Drift/Drag want the bias.
      if (d.oversteerProne && goal !== "Drift" && goal !== "Drag") { center -= 14; rA -= 22; rD -= 12; }
      return {
        driveline: "AWD",
        accel: rEven(clamp(rA, 0, 100)), decel: rInt(clamp(rD, 0, 100)),
        frontAccel: rEven(clamp(fA, 0, 95)), frontDecel: rInt(clamp(fD, 0, 100)),
        centerRear: rInt(clamp(center, 50, 90)),
        why: diffWhy("AWD", goal, rEven(clamp(rA, 0, 100)), rInt(clamp(rD, 0, 100)), i, rInt(clamp(center, 50, 90))),
      };
    }
    // RWD
    const r = DG.rwd; // [accelAdj, decelAdj, accelOverride?, decelOverride?]
    let accel = r[2] != null ? r[2] : 55 + trim + (r[0] || 0) + ptAccel;
    let decel = r[3] != null ? r[3] : 20 + (r[1] || 0);
    if (i.engineLocation !== "Front") accel -= 4;
    if (lowGrip) accel -= 6;
    accel = rEven(clamp(accel, 0, 100)); decel = rInt(clamp(decel, 0, 100));
    return { driveline: "RWD", accel, decel, why: diffWhy("RWD", goal, accel, decel, i) };
  }

  function diffWhy(dl, goal, accel, decel, i, center) {
    let t = "";
    if (dl === "AWD") t = center >= 70
      ? `AWD runs three diffs. Center sends ${center}% torque to the rear (floored at 50%) so it rotates like a sharpened RWD; rear locks harder than front. ${goalName(goal)}: rear ${accel}/${decel}%.`
      : `AWD runs three diffs. Center sends ${center}% torque to the rear — held closer to balanced to settle a tail-happy chassis; rear locks ${accel}/${decel}%. ${goalName(goal)}.`;
    else if (dl === "FWD") t = `Front accel ${accel}% (capped 95) puts power down without killing turn-in; decel ${decel}% keeps it stable on lift. ${goalName(goal)} tune.`;
    else t = `Accel lock ${accel}% controls corner-exit traction; decel ${decel}% sets off-throttle stability. ${goalName(goal)}` + (goal === "Drift" ? " runs a near-welded rear to hold angle." : goal === "Drag" ? " locks both rears for an even launch." : ".");
    return { text: t, formula: `${dl} base ± power-trim ± powertrain ± ${goalName(goal)} adj ; accel even %, clamp 0–100` };
  }

  /* =====================================================================
     HANDLING BIAS  (Understeer −5 … 0 … +5 Oversteer)
     ---------------------------------------------------------------------
     Applied AS A POST-PROCESS on top of the fully-computed per-goal tune.
     At bias === 0 this is NEVER called, so every baseline value is returned
     byte-for-byte. +bias shifts balance toward OVERSTEER (free the rear,
     plant the front); −bias toward UNDERSTEER (plant the rear, free front).
     Each lever uses its own non-linear curve so the dial is gentle near 0
     and aggressive at the extremes, then is re-clamped to its legal range
     and never erases the goal's character (deltas are small relative to the
     per-goal spread). A one-line note is appended to the relevant why.text.
     ===================================================================== */
  // signed non-linear scale: sign(b) · (|b|/5)^exp   →   −1 … +1
  function biasScale(b, exp) {
    const m = Math.pow(Math.min(Math.abs(b), 5) / 5, exp);
    return b < 0 ? -m : m;
  }
  const biasNote = (why, msg) => { if (why) why.text += "  Handling bias: " + msg + "."; };
  const dirWord = (b) => (b > 0 ? "oversteer" : "understeer");

  function applyHandlingBias(t, input, bias) {
    const d = t.derived;

    /* ---- ARB front/rear ratio  (±8% each end, exp 1.2) ---- */
    if (t.arb && d.canTuneSusp) {
      const s = biasScale(bias, 1.2);              // + → softer front / stiffer rear
      t.arb.front = clamp(r2(t.arb.front * (1 - 0.08 * s)), 1, 65);
      t.arb.rear  = clamp(r2(t.arb.rear  * (1 + 0.08 * s)), 1, 65);
      biasNote(t.arb.why, bias > 0
        ? "front bar softened / rear bar stiffened → less front roll-grip used, freer rotation → promotes oversteer"
        : "front bar stiffened / rear bar softened → more rear plant, calmer nose → promotes understeer");
    }

    /* ---- Front/rear spring ratio  (front ±12%, rear ±4%, exp 1.1) ---- */
    if (t.springs && d.canTuneSusp) {
      const s = biasScale(bias, 1.1);              // + → softer front / stiffer rear
      const minF = input.springRateMinF != null ? input.springRateMinF : input.springRateMin;
      const maxF = input.springRateMaxF != null ? input.springRateMaxF : input.springRateMax;
      const minR = input.springRateMinR != null ? input.springRateMinR : input.springRateMin;
      const maxR = input.springRateMaxR != null ? input.springRateMaxR : input.springRateMax;
      t.springs.front = rInt(clamp(t.springs.front * (1 - 0.12 * s), minF, maxF));
      t.springs.rear  = rInt(clamp(t.springs.rear  * (1 + 0.04 * s), minR, maxR));
      biasNote(t.springs.why, bias > 0
        ? "front springs softened ~12% / rear stiffened ~4% → rear-biased frequency, front loads up → promotes oversteer"
        : "front springs stiffened ~12% / rear softened ~4% → front-biased frequency, rear plants → promotes understeer");
    }

    /* ---- Differential ---- */
    if (t.differential) {
      const dl = t.differential.driveline;
      const isDrift = t.goal === "Drift";
      const sA = biasScale(bias, 1.4);             // accel: ±12 pts
      const sD = biasScale(bias, 1.2);             // decel: ±8 pts
      // Drift welds the drive axle (accel override 100 / high decel) — leave its
      // accel/decel character intact, but still let the AWD center diff move.
      let lockMoved = false, centerMoved = false;
      if (!isDrift && (dl === "RWD" || dl === "AWD")) {
        t.differential.accel = rEven(clamp(t.differential.accel + 12 * sA, 0, 100));
        t.differential.decel = rInt(clamp(t.differential.decel + 8 * sD, 0, 100));
        lockMoved = true;
      } else if (!isDrift && dl === "FWD") {
        t.differential.accel = rEven(clamp(t.differential.accel + 12 * sA, 0, 95));
        t.differential.decel = rInt(clamp(t.differential.decel + 8 * sD, 5, 100));
        lockMoved = true;
      }
      if (dl === "AWD" && t.differential.centerRear != null) {
        // AWD center (rear torque %): +6 pts, exp 1.1, hard floor 50 / ceil 90
        const sC = biasScale(bias, 1.1);
        t.differential.centerRear = rInt(clamp(t.differential.centerRear + 6 * sC, 50, 90));
        centerMoved = true;
      }
      if (lockMoved || centerMoved) {
        const parts = [];
        if (lockMoved) parts.push("accel/decel lock " + (bias > 0 ? "raised" : "lowered"));
        if (centerMoved) parts.push("torque sent " + (bias > 0 ? "further rearward" : "forward"));
        biasNote(t.differential.why, parts.join(" and ") +
          (bias > 0 ? " → tighter/more rearward drive → tuned toward oversteer"
                    : " → looser/more frontward drive → tuned toward understeer"));
      }
    }

    /* ---- Brake balance  (±4 pts, linear; NOT applied to Drift) ---- */
    if (t.braking && t.goal !== "Drift") {
      const s = bias / 5;                          // linear per spec
      t.braking.balance = rInt(clamp(t.braking.balance + 4 * s, 40, 65));
      biasNote(t.braking.why, bias > 0
        ? "brake balance shifted forward → front loads first under braking, calmer rear → entry tuned toward oversteer control with front grip"
        : "brake balance shifted rearward → rear eases off, front frees → entry tuned toward understeer reduction");
    }

    /* ---- Aero front/rear ratio  (±8 pts each end, exp 1.05) ---- */
    if (t.aero && t.aero.applicable && t.goal !== "Drag") {
      const s = biasScale(bias, 1.05);             // + → more front DF / less rear DF
      const fR = input.aeroFront || [null, null], rR = input.aeroRear || [null, null];
      const hasR = (rng) => rng[0] != null && rng[1] != null;
      const toLbf = (pct, rng) => hasR(rng) ? Math.round(rng[0] + (rng[1] - rng[0]) * clamp(pct / 100, 0, 1)) : null;
      let moved = false;
      if (t.aero.front != null) {
        t.aero.front = r5(clamp(t.aero.front + 8 * s, 0, 100));
        t.aero.frontLbf = toLbf(t.aero.front, fR);
        moved = true;
      }
      if (t.aero.rear != null) {
        t.aero.rear = r5(clamp(t.aero.rear - 8 * s, 0, 100));
        t.aero.rearLbf = toLbf(t.aero.rear, rR);
        moved = true;
      }
      if (moved) biasNote(t.aero.why, bias > 0
        ? "front downforce raised / rear lowered → more front high-speed grip, looser rear → high-speed balance toward oversteer"
        : "front downforce lowered / rear raised → rear planted at speed, lighter nose → high-speed balance toward understeer");
    }

    return t;
  }

  /* =====================================================================
     OVERALL STIFFNESS  (Soft −5 … 0 … +5 Hard)
     ---------------------------------------------------------------------
     The MAGNITUDE companion to Handling Bias. Where bias shifts the front/rear
     RATIO (opposite directions each end), stiffness scales suspension FIRMNESS
     up or down THE SAME direction at both ends, so it leaves handling balance
     untouched — the two dials are orthogonal and compose cleanly.

     Applied AS A POST-PROCESS on top of the fully-computed per-goal tune. At
     stiff === 0 this is NEVER called, so every baseline value is returned
     byte-for-byte (same hard contract as bias). +stiff (Hard) raises spring
     rate, anti-roll bars and damping and drops ride height toward the floor;
     −stiff (Soft) does the mirror. Each lever uses the shared non-linear
     biasScale curve (gentle near 0, firmer at the extremes) and is re-clamped
     to its legal range. Stock suspension exempts every lever (nothing to tune).
     ===================================================================== */
  const stiffNote = (why, msg) => { if (why) why.text += "  Overall stiffness: " + msg + "."; };

  function applyOverallStiffness(t, input, stiff) {
    const d = t.derived;
    // Stock suspension locks springs / bars / dampers / ride height in-game, so
    // there is nothing for the firmness dial to move — leave the tune untouched.
    if (!d.canTuneSusp) return t;
    const hard = stiff > 0;

    /* ---- Spring rate front & rear (±25% each, exp 1.1) + ride height ---- */
    if (t.springs) {
      const s = biasScale(stiff, 1.1);             // + → stiffer / lower
      const minF = input.springRateMinF != null ? input.springRateMinF : input.springRateMin;
      const maxF = input.springRateMaxF != null ? input.springRateMaxF : input.springRateMax;
      const minR = input.springRateMinR != null ? input.springRateMinR : input.springRateMin;
      const maxR = input.springRateMaxR != null ? input.springRateMaxR : input.springRateMax;
      t.springs.front = rInt(clamp(t.springs.front * (1 + 0.25 * s), minF, maxF));
      t.springs.rear  = rInt(clamp(t.springs.rear  * (1 + 0.25 * s), minR, maxR));
      // Ride height: harder → lower toward the floor, softer → taller. Grounded in
      // the goal data (stiff tarmac goals sit at the ride-height floor, soft
      // off-road goals sit tall) and the anti-bottoming physics (soft springs need
      // more travel). Both ends move together so balance is preserved.
      const rhMinF = input.rideHeightMinF != null ? input.rideHeightMinF : input.rideHeightMin;
      const rhMaxF = input.rideHeightMaxF != null ? input.rideHeightMaxF : input.rideHeightMax;
      const rhMinR = input.rideHeightMinR != null ? input.rideHeightMinR : input.rideHeightMin;
      const rhMaxR = input.rideHeightMaxR != null ? input.rideHeightMaxR : input.rideHeightMax;
      t.springs.rideF = r1(clamp(t.springs.rideF - (rhMaxF - rhMinF) * 0.15 * s, rhMinF, rhMaxF));
      t.springs.rideR = r1(clamp(t.springs.rideR - (rhMaxR - rhMinR) * 0.15 * s, rhMinR, rhMaxR));
      stiffNote(t.springs.why, hard
        ? "spring rates raised ~25% and ride height lowered → a firmer, lower, flatter platform"
        : "spring rates lowered ~25% and ride height raised → a softer, taller, more compliant platform");
    }

    /* ---- Anti-roll bars front & rear (±30% each, exp 1.15) ---- */
    if (t.arb) {
      const s = biasScale(stiff, 1.15);
      t.arb.front = clamp(r2(t.arb.front * (1 + 0.30 * s)), 1, 65);
      t.arb.rear  = clamp(r2(t.arb.rear  * (1 + 0.30 * s)), 1, 65);
      stiffNote(t.arb.why, hard
        ? "both anti-roll bars stiffened ~30% → less body roll, sharper turn-in"
        : "both anti-roll bars softened ~30% → more roll, more mechanical grip over bumps");
    }

    /* ---- Damping: bump + rebound, all four corners (±25%, exp 1.1) ---- */
    // Scaling bump and rebound by the same factor keeps their ratio intact.
    if (t.damping) {
      const k = 1 + 0.25 * biasScale(stiff, 1.1);
      t.damping.bumpF    = r1(clamp(t.damping.bumpF * k, 1, 20));
      t.damping.bumpR    = r1(clamp(t.damping.bumpR * k, 1, 20));
      t.damping.reboundF = r1(clamp(t.damping.reboundF * k, 1, 20));
      t.damping.reboundR = r1(clamp(t.damping.reboundR * k, 1, 20));
      stiffNote(t.damping.why, hard
        ? "bump and rebound raised ~25% in proportion → tighter body control"
        : "bump and rebound lowered ~25% in proportion → softer, more absorbent damping");
    }

    return t;
  }

  /* =====================================================================
     VALIDATE — guard against nonsense input before computing a tune.
     Returns { valid:boolean, errors:string[] }. compute() stays defensive
     (clamps everything) but the UI uses this to refuse to render a tune
     built from garbage. Only flags inputs that would make a tune
     meaningless or physically impossible; optional fields are allowed blank.
     ===================================================================== */
  function validate(input) {
    const errors = [];
    const i = input || {};
    const isNum = (v) => typeof v === "number" && isFinite(v);

    // required numerics must be present & finite
    const required = {
      power: "Power (hp)",
      weight: "Weight (lb)",
      frontWeightPct: "Front weight %",
    };
    Object.keys(required).forEach((key) => {
      if (i[key] === undefined || i[key] === null || i[key] === "" || !isNum(Number(i[key]))) {
        errors.push(`${required[key]} is required and must be a number.`);
      }
    });

    // weight must be a positive mass
    if (isNum(Number(i.weight)) && Number(i.weight) <= 0) {
      errors.push("Weight must be greater than 0.");
    }
    // front weight % must be a real split strictly inside 0–100
    if (isNum(Number(i.frontWeightPct))) {
      const fw = Number(i.frontWeightPct);
      if (fw <= 0) errors.push("Front weight % must be greater than 0.");
      else if (fw >= 100) errors.push("Front weight % must be less than 100.");
    }
    // power can't be negative (0 is allowed: a rolling chassis still gets a tune)
    if (isNum(Number(i.power)) && Number(i.power) < 0) {
      errors.push("Power cannot be negative.");
    }
    // torque, when supplied, can't be negative
    if (i.torque !== undefined && i.torque !== null && i.torque !== "" && isNum(Number(i.torque)) && Number(i.torque) < 0) {
      errors.push("Torque cannot be negative.");
    }
    // gears: at least 1 (EVs are forced single-speed downstream, but the field still can't be <1)
    if (i.gears !== undefined && i.gears !== null && i.gears !== "") {
      const g = Number(i.gears);
      if (!isNum(g)) errors.push("Number of gears must be a number.");
      else if (g < 1) errors.push("Number of gears must be at least 1.");
    }
    // spring-rate and ride-height ranges are per-axle (front/rear) and may differ
    // in-game; fall back to the legacy shared key when an axle value is absent.
    // When both ends of a span are supplied it must be real (positive, non-inverted).
    const pick = (a, b) => (a !== undefined && a !== null && a !== "" ? a : b);
    const checkRange = (loRaw, hiRaw, label) => {
      const lo = Number(loRaw), hi = Number(hiRaw);
      if (isNum(lo) && isNum(hi)) {
        if (lo <= 0) errors.push(`${label} min must be greater than 0.`);
        if (hi < lo) errors.push(`${label} max must be greater than or equal to ${label.toLowerCase()} min.`);
      }
    };
    checkRange(pick(i.springRateMinF, i.springRateMin), pick(i.springRateMaxF, i.springRateMax), "Front spring rate");
    checkRange(pick(i.springRateMinR, i.springRateMin), pick(i.springRateMaxR, i.springRateMax), "Rear spring rate");
    checkRange(pick(i.rideHeightMinF, i.rideHeightMin), pick(i.rideHeightMaxF, i.rideHeightMax), "Front ride height");
    checkRange(pick(i.rideHeightMinR, i.rideHeightMin), pick(i.rideHeightMaxR, i.rideHeightMax), "Rear ride height");

    return { valid: errors.length === 0, errors };
  }

  /* =====================================================================
     TOP-LEVEL COMPUTE
     ===================================================================== */
  function compute(input, goal) {
    const d = derive(input);
    const spr = springs(input, d, goal);
    const tune = {
      goal, derived: d,
      summary: [
        { k: "Power-to-weight", v: `${r2(d.pw)} hp/lb` },
        { k: "Front corner", v: `${rInt(d.frontCorner)} lb` },
        { k: "Rear corner", v: `${rInt(d.rearCorner)} lb` },
        { k: "Balance", v: `${r1(d.frac * 100)}/${r1(d.rearFrac * 100)}` },
        { k: "Class tier", v: d.classTier },
      ],
      tires: tires(input, d, goal),
      gearing: gearing(input, d, goal),
      alignment: alignment(input, d, goal),
      arb: arb(input, d, goal),
      springs: spr,
      damping: damping(input, d, goal, spr),
      aero: aero(input, d, goal),
      braking: braking(input, d, goal),
      differential: differential(input, d, goal),
    };
    // Post-process dials. At 0 each is skipped, so the baseline is returned
    // byte-for-byte. Stiffness (magnitude) runs first to set how firm the
    // platform is, then handling bias (balance) shifts the front/rear ratio on
    // top — the two are orthogonal so the order only matters at the clamps.
    const stiff = Number(input.overallStiffness) || 0;
    if (stiff !== 0) applyOverallStiffness(tune, input, clamp(stiff, -5, 5));
    const bias = Number(input.handlingBias) || 0;
    if (bias !== 0) applyHandlingBias(tune, input, clamp(bias, -5, 5));
    return tune;
  }

  // Overall (rolling) tire diameter in inches from the FH6 tire spec the game
  // prints on its Tires screen — WIDTH/ASPECT R RIM, e.g. 315/30R17 → 24.44".
  // width in mm, aspect % (sidewall height as % of width), rim in inches.
  // Ø = rim + 2 × sidewall, where sidewall(mm) = width × aspect/100, mm→in via /25.4.
  // Returns null on any non-positive/blank part so callers fall back to the HP heuristic.
  function overallTireDiameter(widthMm, aspectPct, rimIn) {
    const w = Number(widthMm), a = Number(aspectPct), r = Number(rimIn);
    if (!(w > 0) || !(a > 0) || !(r > 0)) return null;
    return r + 2 * (w * (a / 100)) / 25.4;
  }

  const API = { GOALS, GOAL_META, compute, validate, overallTireDiameter };
  if (typeof window !== "undefined") window.TUNING = API;
  if (typeof module !== "undefined" && module.exports) module.exports = API;
})();
