# FH6 Tuning Calculator — Calculation Audit Report

**Scope:** every output category of the pure engine `tuning.js`
(`window.TUNING.compute(input, goal)`), audited against the authoritative
research in `research/` and re-verified by a Node invariant sweep over 27 car
configs (a drivetrain × powertrain × engine-location grid) × 6 goals × the full
−5…+5 bias range in 0.5 steps (3,969 `compute()` calls, 239,562 individual
output checks).

**Sweep result:** 0 errors — no NaN, every output in its legal slider range,
multi-gear ratios strictly descending, bias-0 byte-for-byte equal to the no-bias
baseline, goal distinctness 27/27, drivetrain distinctness 27/27. Harness:
`sweep.js` (re-run with `node sweep.js`); the example-based suite lives in
`test/` (`node --test`, 91 tests).

**Status legend:** ✔ = correct as implemented · ✦ = had a high/medium issue,
now fixed · (#) = community-heuristic lever with no single published source.

---

## Summary table

| Parameter | Formula Used | Source | In-Range? | Goal-Sensitive? | Issues Found |
|---|---|---|---|---|---|
| **Tires (F/R psi)** | `psi = compoundBase + (weight−3000)/1323 + classBonus ± split/2 + aero/width + goal override`; clamp 15–55, step 0.5 | spec-tires-braking §1.1–1.9; findings.json (ForzaFire/SkyCoach) | ✔ 15–55 | ✔ | ✦ 1 med (engine-location term over-widened the F/R spread vs drivetrain-only references) **FIXED** — removed; 2 low (doc rounding nit; `Offroad`/`OffRoad` key naming). |
| **Gearing (FD + ratios)** | `FD = 4.25 + clamp((400−hp)/600,±0.6) + weightAdj + goalFD + aero/dt/engine/EV mods`; `Rₙ = A·n^B`, strictly descending; FD∈[2,7], gear∈[0.5,5.5] | spec-gearing §1–7; findings.json | ✔ | ✔ | ✦ 1 high (EV single ratio hardcoded 1.30) **FIXED**; ✦ 1 med (FWD/AWD FD direction wording) **FIXED**; 2 low (no advisory gear count / out-of-range FD note) — accepted. |
| **Alignment — Camber F/R** | `camF = −(0.6+(grip−0.45)/0.55×1.4) + dtBias + loadTrim → goal`; `camR = camF×0.55 + dtBias − rearLoad → goal`; round 0.1, clamp −5..0 | spec-alignment-arb §1.1–1.2; findings.json | ✔ −5..0 | ✔ | None |
| **Alignment — Toe F/R** | per-goal toe table (− out / + in), RWD high-torque +0.1 rear; clamp −5..5 | spec-alignment-arb §1.3–1.4; critique #9, #10 | ✔ −5..5 | ✔ | ✦ 1 med (OffRoad front toe collapsed onto Circuit/Drag) **FIXED**; 1 low (Circuit −0.05 deviation) — documented. |
| **Alignment — Caster** | `5.0 + clamp((wt−2400)/1800,0,1)×2 + classBump + aero + goal`; clamp 1–7, grip-goal floor 5 | spec-alignment-arb §1.5 | ✔ 1–7 | ✔ | None |
| **Anti-Roll Bars (F/R)** | `base = (wt/2)/(200−200·stiff%)`; `front=base+split/2`, `rear=base−split/2`; ×goal[ ]; FWD/AWD/engine/aero/EV trims; clamp 1–65 | spec-alignment-arb §2; ForzaFire (findings.json) | ✔ 1–65 | ✔ | None |
| **Springs (F/R rate)** | `K = f²·Wcorner/9.78` (f from class+aero+goal+dt split); weight-dist fine-tune; EV×1.08/Hybrid×1.04; support floor; clamp part min/max | spec-springs-damping §1 | ✔ part min/max | ✔ | None |
| **Ride Height (F/R)** | `ride = rhMin + (rhMax−rhMin)·(base[goal]+comp+aero+softFront+heavy)`; clamp min/max, round 0.1 | spec-springs-damping §2 | ✔ part min/max | ✔ | None (drivetrain-insensitive by design) |
| **Damping (reb/bump F/R)** | `fBump=MinBump(class)+frontAxle/200×0.1`, `fReb=fBump/0.6`; rear offset by spring Δ%; ×goal mults; dt/engine/EV/Drag-RWD trims; ratio guard (bypassed Drag-RWD/OffRoad/Rally); clamp 1–20 | spec-springs-damping §3; findings.json | ✔ 1–20 | ✔ | None |
| **Aero (F/R %, lbf)** | `LEVEL[goal] ± balanceShift(dt) + weightTrim`; high-power scale; AWD-circuit / engine / Drag overrides; %→lbf over entered range; clamp 0–100% | spec-aero-diff §1; findings.json | ✔ 0–100% | ✔ | ✦ 1 med (weight-trim coeff 0.006 undershot 1.87 lbf/%) **FIXED**; 1 low (r5 granularity) — accepted. |
| **Braking (balance/pressure)** | `bias = dtBase + clamp((fwPct−50)×0.5,±6) + engine + EVregen(by axle) + capped(surface+goal)`; Drift=48; clamp 40–65. `pres = 100 + massAdj + compound + EV + goal`; clamp 80–130 step 5 | spec-tires-braking §2.A–2.B; findings.json | ✔ 40–65 / 80–130 | ✔ | None |
| **Differential (per axle + AWD center)** | per-dt base ± power-trim ± powertrain ± goal; AWD center = base + clamp((fwPct−50)×0.4,−6,8) + powerBonus; accel even%, FWD cap 95 / floor 5, center 50–90 | spec-aero-diff §2; findings.json | ✔ 0–100 / 50–90 | ✔ | None |
| **Handling-bias slider (−5…+5)** (#) | post-process: `biasScale(b,exp)=sign(b)·(|b|/5)^exp` × per-lever magnitude on ARB/springs/diff/brakes/aero; re-clamped; **bias 0 = no-op** | **No single published source — pure calculator synthesis**, now documented in `research/spec-handling-bias.md` | ✔ all levers re-clamped | ✔ | ✦ 1 med (undocumented) **FIXED** (spec written); 3 low (Drift/Drag exemptions, center exp) — documented. |

---

## Per-category notes & research trace

**Tires.** Cold-pressure model traces directly to `spec-tires-braking.md` §1.1–1.8
and the FH6 baselines in `findings.json` (ForzaFire/SkyCoach): a compound base
psi, a `(weight−3000)/1323` mass term (≈ +1 psi / 600 kg), a high-class bonus, a
drivetrain front/rear split, and mutually-exclusive
per-goal overrides (Drag floods the driven axle, Drift runs 30/27, OffRoad/Rally
drop for a bigger patch). Clamp 15–55, step 0.5. **Engine location intentionally
does not bias tire pressure** (removed 2026-06): it over-widened the front/rear
spread relative to the drivetrain-only references — an AWD rear-engine car spread
~1.5 psi where AWD guidance is ~0.2–0.5 — and pointed the wrong way, since a
rear-heavy car's loaded axle wants equal-or-more pressure, not less. The
drivetrain split alone now governs the F/R delta (FWD 1.5 / RWD 0.75 / AWD 0.35,
all inside ForzaFire's ranges). Two low nits remain: a prose
rounding error in the spec's Drag example (now corrected to F 35.5 / R 22.5) and
the deliberate `tireCompound:'Offroad'` vs `goal:'OffRoad'` key distinction
(safe via fallthrough default, noted in code).

**Gearing.** Final drive is the community-canon `4.25 + (400−hp)/600` anchored at
400 hp, with the discontinuous halve/double thresholds already replaced by a
continuous `±0.6` clamp (critique #4), plus weight, goal, aero, drivetrain,
engine-location and EV modifiers (`spec-gearing.md` §1–3). Ratios follow the
power-series `Rₙ = A·nᴮ` with strict-descending enforcement (§4, critique #7).
**Two fixes applied:** the EV single ratio (high issue) no longer returns a
hardcoded 1.30 — it scales with power-to-weight as
`clamp(1.20 + (1 − pw/0.40)×0.30, 0.90, 1.60)`, honoring §4's "single fixed-ratio
tuned to the car" guidance and critique #8's "top-gear-region, not a launch
ratio"; and the FWD/AWD `fd += 0.05` (medium wording issue) is kept (it is
correct in-game — a *longer* top gear absorbs driveline loss while launch is
traction-limited) with the contradictory "shorter" language corrected in both the
code comment and `spec-gearing.md` §3 + the edge-case table.

**Alignment — camber.** Front camber from the tire grip factor mapped −0.6…−2.0,
shifted by drivetrain (RWD more negative, FWD less) and front load, then a
per-goal override/offset table; rear ≈ 55 % of front with its own goal table and
a mid/rear-engine −0.2 trim. Round 0.1, clamp −5..0. Matches
`spec-alignment-arb.md` §1.1–1.2 worked examples exactly. No issues.

**Alignment — toe & caster.** Per-goal toe tables with an understeer-prone branch
(FWD or ≥55 % front) and a high-torque-RWD rear-toe-in bump; caster scales with
weight + PI class + aero with a grip-goal floor of 5°. **One fix applied:**
OffRoad front toe was `0.0`, identical to Circuit/Drag for non-understeer-prone
cars (critique #9). It is now `-0.2` (loose-surface toe-out), making
Circuit/Drag/OffRoad distinct; spec §1.3 table + code updated to match. The
Circuit `-0.05` grip-car default is an intentional critique-#9 nudge (it snaps to
0.0° at the 0.1° slider step, documented in code).

**Anti-roll bars.** ForzaFire base `(weight/2)/(200−200·stiff%)` with a
drivetrain-weighted split, per-goal multipliers, and FWD/AWD/engine/aero/EV
edge-case trims; clamp 1–65 (`spec-alignment-arb.md` §2, broad community
consensus in `findings.json`). No issues.

**Springs & ride height.** Ride-frequency model `K = f²·Wcorner/9.78` (the
9.78 coefficient is the critique-#1 correction), frequencies by class tier with
aero +0.6 Hz, per-goal offsets/multipliers, drivetrain split, weight-distribution
fine-tune, EV/Hybrid multipliers, and a physics support-floor sanity check
(`spec-springs-damping.md` §1). Ride height is the deterministic slider-fraction
method from §2 (qualitative "start low, raise for clearance" → goal base
fractions + compound/aero/soft-front/heavy boosts). No issues.

**Damping.** Bump from `MinBump(class) + frontAxle/200×0.1`, rebound at bump/0.6,
a 4-bracket rear offset by spring-rate Δ%, per-goal multipliers, drivetrain and
Drag-RWD/engine/EV trims, OffRoad/Rally bump floor, and a bump-to-rebound ratio
guard that is bypassed for the intentionally-asymmetric goals (critique #6).
Clamp 1–20. Traces to `spec-springs-damping.md` §3 + `findings.json`. No issues.

**Aero.** Per-goal downforce `LEVEL`, a drivetrain balance shift, a weight-bias
rear trim, high-power scaling, and AWD-circuit / engine-location / Drag
overrides, mapped from % into the entered lbf range (`spec-aero-diff.md` §1,
`findings.json`). **One fix applied:** the rear weight trim used `0.006`/% which
undershot the documented `+1.87 lbf rear per 1 % front-weight above 47 %` by
~19 %. It now computes `(frontWeightPct − 47) × 1.87 / rearSpan` against the
actual entered rear span (fallback 250 lbf standard span), so a 47→57 % swing now
adds ~18 lbf rear (target ~18.7), matching §1.2 and the `findings.json` balance
example. The low `r5` granularity note is accepted (the 5 % slider step is what
the game exposes).

**Braking.** Drivetrain base bias + front-weight term + engine-location +
axle-aware EV regen (critique #13) + a capped surface/goal rearward shift
(critique #12), with the Drift 48 % override; pressure from mass + compound + EV
+ goal. Clamps 40–65 and 80–130 step 5 (`spec-tires-braking.md` §2.A–2.B,
`findings.json`). No issues.

**Differential.** Per-drivetrain baselines with a power-trim (`−((pw−0.13)/0.05)×6`,
clamped −16..10), powertrain accel deltas (EV −6 / Hybrid −3, critique #5),
per-goal adjustment tables, FWD caps (accel ≤95, decel ≥5), and the AWD center
split (base + front-weight term + high-power bonus, floored 50 / capped 90).
Accel snaps even, decel/center 1 % (`spec-aero-diff.md` §2, `findings.json`).
No issues.

**Handling-bias slider.** This is the one lever with **no single published
community source** — it is a *pure calculator synthesis* that bundles five
individually-researched balance levers (ARB, springs, differential, brake
balance, aero) into one understeer↔oversteer dial. Each lever still moves in its
researched direction and is re-clamped to its legal range; the dial uses
per-lever non-linear curves `sign(b)·(|b|/5)^exp` (exp: ARB 1.2, springs 1.1,
diff accel 1.4 / decel 1.2 / center 1.1, brakes 1.0 linear, aero 1.05) so it is
gentle near center and firmer at the extremes. The **bias = 0 case is a hard
no-op** (the post-process is skipped entirely, so every baseline value returns
byte-for-byte — verified across all 51,840 sweep cases). **Fix applied:** the
previously-undocumented synthesis is now fully documented in
`research/spec-handling-bias.md` (rationale, per-lever exponents and why they
differ, the Drift brake/lock and Drag aero exemptions, a worked example, and an
explicit "pure calculator synthesis — not a transcription of any single source"
provenance note). The three remaining low items (Drift exemptions, center
exponent rationale) are all covered there.

---

## Fixes applied (high/medium)

1. **Gearing — EV single ratio (HIGH).** `tuning.js`: replaced the hardcoded
   `evRatio = 1.30` with `r2(clamp(1.20 + (1 − d.pw/0.40)×0.30, 0.90, 1.60))`,
   so the lone gear scales with power-to-weight and sits in the top-gear region.
   Target-top-speed back-solve and `why`/formula text updated accordingly.
   *Verified:* low-pw 4500 lb/200 hp → 1.47; high-pw 2600 lb/900 hp → 1.24; both
   in 0.9–1.6; target back-solve still hits 180 mph exactly.

2. **Gearing — FWD/AWD FD direction (MEDIUM).** Kept the correct `fd += 0.05`
   (longer top gear compensates for driveline loss; launch is traction-limited),
   corrected the contradictory "shorter" wording in the `tuning.js` comment and
   in `spec-gearing.md` §3 + the edge-case table.

3. **Alignment — OffRoad front toe (MEDIUM).** `tuning.js`: OffRoad front toe
   `0.0 → −0.2` (critique #9), making Circuit/Drag/OffRoad distinct for
   non-understeer-prone cars. `spec-alignment-arb.md` §1.3 table and code block
   updated. *Verified:* Circuit 0.0 / Drag 0.0 / OffRoad −0.2 for a 50 %-front RWD.

4. **Aero — rear weight-trim coefficient (MEDIUM).** `tuning.js`: replaced
   `(frontWeightPct − 47) × 0.006` with `(frontWeightPct − 47) × 1.87 / rearSpan`
   (actual entered rear span, fallback 250 lbf), matching `spec-aero-diff.md` §1.2
   / `findings.json`. *Verified:* 47→57 % now adds 18 lbf rear (target ~18.7).

5. **Handling bias — missing research backing (MEDIUM).** Created
   `research/spec-handling-bias.md` documenting rationale, per-lever exponents
   and magnitudes (and why they differ), goal exemptions, a worked example, and
   an explicit pure-synthesis provenance note. No code change (formula was
   already correct and bias-0-neutral).

**Low-severity doc touch-ups (optional, applied):** `spec-tires-braking.md` Drag
example rounding corrected to F 35.5 / R 22.5.

---

## Verification

```
$ node sweep.js
=== SWEEP RESULTS ===
car configs: 27
compute() calls: 3969
individual output checks: 239562
goal-distinct configs OK: 27/27
drivetrain-distinct cars OK: 27/27
errors: 0
ALL CHECKS PASSED ✓
```

Checks enforced by the sweep: no NaN / non-finite output anywhere; every numeric
output inside its legal Forza slider range (psi 15–55, FD 2–7, gear 0.5–5.5,
camber −5..0, toe −5..5, caster 1–7, ARB 1–65, damping 1–20, aero 0–100 %, brake
balance 40–65, brake pressure 80–130, diff 0–100, AWD center 50–90); multi-gear
ratios strictly descending; `handlingBias = 0` identical to the no-bias baseline;
all 6 goals distinct per config (27/27); all three drivetrains distinct per car
(27/27).
