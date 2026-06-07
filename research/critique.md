I have completed a thorough verification of every formula and worked example. Here is my review.

---

# FH6 Tuning Calculator Spec Review — Corrections (Prioritized)

## P0 — Bugs that produce WRONG NUMBERS or crash

**1. Spring-rate frequency coefficient is off by 2× (springs too soft).**
`K = f² × M / 19.56` is wrong. The correct imperial ride-frequency relation is `f = 3.127·√(K/W)`, so `K = W·f²/9.78`. The constant must be **9.78, not 19.56**. The spec's own derivation note ("inverse of `Hz = sqrt(K/M × 19.56)`") is self-consistent but starts from the wrong forward formula — `19.56` would only be correct if it were `2×g/(2π)²`-style, which it isn't. Impact: the worked example yields Front 358/Rear 264 lb/in; corrected it's ~**717/527 lb/in**. Every spring output is half what it should be (and will frequently clamp to `springMin`, masking the bug). **Fix:** change divisor to `9.78` in §1.5 and §4 of springs-damping, and re-derive the example.

**2. `computeAero()` references `engineLocation` and `powertrain` but never destructures them.** In aero-diff §1.1, the function signature destructures only `{ aeroInstalled, hasRearWing, drivetrain, goal, weight, frontMin, frontMax, rearMin, rearMax, frontWeightPct, power }`, yet the body uses `engineLocation` (Rear/Mid trim) and reads `powertrain` in prose. **Fix:** add `engineLocation, powertrain` to the destructure.

**3. `hasRearWing` undefined → rear downforce always null.** `if (!hasRearWing) rearDF = null;` runs whenever the caller doesn't pass `hasRearWing`. **Fix:** default `hasRearWing = aeroInstalled` (i.e., assume a full kit unless explicitly splitter-only).

**4. FD formula has a discontinuity + collapse at the ≥800 / ≤200 hp thresholds.** `effectivePower` halves/doubles, so 799 hp → FD 3.58 but 800 hp → FD 4.25 (a backwards jump — more power giving *shorter* gearing). Both extremes collapse onto 4.25. **Fix:** replace the hard halve/double with a continuous taper, e.g. clamp the FD term instead: `fd = 4.25 + clamp((400 - power)/600, -0.60, +0.60)` then apply weight adj. This removes the cliff and keeps high-hp cars taller-geared monotonically.

**5. EV `ptAccel` / `ptHybrid` differential trim is specified in prose (§2.5) but never wired into `computeDifferential()` (§2.3).** The function body computes `trim` (power) but not the powertrain adjustment. **Fix:** inside each branch, before `clampEven`, add `accel += ptAccel` (and `*0.5` for AWD front) where `const ptAccel = powertrain==='EV'?-6:powertrain==='Hybrid'?-3:0;`.

## P1 — Logic that defeats its own intent

**6. Damping ratio-guard undoes Off-Road/Rally softness.** For Off-Road, bump computes to ~1.46 but `ratioFix` clamps bump to `[0.40·rebound, 0.70·rebound]` = `[1.86, …]`, **raising bump back to 1.86** — the opposite of the intended soft bump. **Fix:** skip `ratioFix` (like the Drag-RWD bypass) for `OffRoad`/`Rally`, OR lower the ratio floor to 0.25 for those goals.

**7. Gear "strict monotonic" enforcement silently fails at the floor.** Once several gears clamp to `GEAR_MIN` (0.50), the `ratios[i] = ratios[i-1] - 0.05` step goes to 0.45, re-clamps to 0.50, and consecutive gears stay equal. For 8–10-speed Drag boxes the top gears are non-strictly-descending. **Fix:** if a computed ratio would breach `GEAR_MIN`, instead compress the *whole set* (scale A or raise FD) so the top gear lands ≥ 0.50 with real spacing; never allow two equal ratios.

**8. EV single-speed ratio uses a launch ratio.** EV returns `[firstGearRatio(pw, goal)]` (~2.4–3.4) as its one gear. A single-speed transmission's lone ratio behaves like a *top* gear and should be tall (~1.0–1.5 region), not a 3.0 launch gear. **Fix:** for EV, return a single ratio around `clamp(1.2 + (1-level)…, 0.9, 1.6)` or simply lean entirely on final drive and set the gear ratio to a fixed ~1.30.

## P2 — Under-differentiated goals (Check #2 failures)

**9. Front toe: Circuit / Drag / Off-Road are identical (0.0°)** for any non-understeer-prone car (RWD, <55% front). **Fix:** give Off-Road a small `-0.2` (or `0.0` but pair with a distinct rear), and keep Drag at exactly `0.0` — but add a Circuit default of `-0.05` for grip cars so the three differ.

**10. Rear toe: Circuit (FWD/AWD) and Off-Road are identical (0.0°).** **Fix:** set Off-Road rear toe to `+0.1` (stability on loose surface) so it differs from a neutral Circuit FWD/AWD.

These are the only two outputs where ≥2 goals collapse to the same value; all other outputs (PSI, springs, ARB, camber, caster, FD, diff, aero, brakes) vary meaningfully across all 6 goals.

## P3 — Consistency / hardening (no wrong numbers today, but fragile)

**11. Goal-key token mismatch.** Code keys are `OffRoad` (capital R) everywhere; prose tables write `Off-Road`. Tire-compound key is `Offroad` (lowercase r). **Fix:** hard-code the input contract: `goal ∈ {Circuit,Drag,Drift,OffRoad,Rally,Touge}`, `tireCompound ∈ {Street,Sport,Race,Rally,Drag,Offroad}`. Note the deliberate `OffRoad` vs `Offroad` distinction in a comment to prevent a `?? default` silently swallowing a typo.

**12. Brake-bias double-count.** A rally tire (`-3`, §2.A.5) plus Rally goal (`-3`, §2.A.6) stacks to `-6`. Likely unintended at full magnitude. **Fix:** if you want both, cap the combined surface/goal rearward shift at `-4`, or drop the compound term when the goal already targets that surface.

**13. Brake-bias EV/regen direction is physically backwards.** §2.A.4 adds **front** bias for EV regen, but regen acts on the *driven* axle — front-drive regen already over-slows the front, so a stability tune wants *less* front friction bias, and rear/AWD-rear regen is the case that wants more front. Current flat `+1% front for all EVs` ignores which axle drives. **Fix:** scale by drive axle: RWD-EV `+1% front`, FWD-EV `−1% front`, AWD-EV `+0.5%`.

**14. Spring support-scaling can still leave car under-supported with no flag.** If `weight/support` scaling pushes past `springMax`, the clamp caps it and `support < weight` persists silently. **Fix:** if after clamping support is still `< weight`, set both springs to `springMax` and emit a "part range insufficient" note.

**15. Engine-location effect on springs is nearly inert.** `effFrontPct` (with `engBias`) feeds only the `devPts` split term, while corner weights use real `frontPct`. Mid/rear cars therefore barely soften the front. **Fix:** apply `engBias` to the corner-weight split too, e.g. compute `frontCornerWt` from `effFrontPct` for the frequency term (keep real `frontPct` for the support check).

## Defaults to hard-code (currently unstated)

- `hasRearWing = aeroInstalled` (fixes #3).
- Spring coefficient `9.78` (fixes #1).
- FD term clamp `±0.60` replacing halve/double (fixes #4).
- `powertrain` accel deltas `{EV:-6, Hybrid:-3, ICE:0}` actually added in diff branches (fixes #5).
- Ratio-guard bypass set: `{Drag-RWD, OffRoad, Rally}` (fixes #6).
- Input enums for `goal`, `tireCompound`, `drivetrain`, `engineLocation`, `powertrain`, `piClass` exactly as keyed in the constant tables (fixes #11).
- Fallback when `piClass` missing: `'A'`/`piIdx=3` (already present); when `tireCompound` missing: `'Sport'`; when `drivetrain` missing: treat as `'RWD'`.

## Verified CORRECT (no change needed)

- All 5 worked examples reproduce exactly **except** spring rates (#1): tire PSI F31.0/R30.5 ✓, drag F35.5 (R rounds to 22.5 not 23.0 — trivial rounding-text nit), brake bias 57 ✓ / pressure 110 ✓, gear series `[3.00,1.91,1.47,1.22,1.05,0.94]` ✓, FD 4.375 ✓ / 3.917 ✓, front camber −2.0 ✓ / drift −4.6 ✓, ARB F17.3/R15.1 ✓, aero F165/R203 ✓, diff accel 52% ✓.
- Diff power-trim sign is correct (high power loosens, low power tightens).
- All 25 required outputs are present and covered.
- Drivetrain routing (RWD rear-only / FWD front-only / AWD 5 values incl. center 50–90) is correct.
- All legal-range clamps are present and, aside from the gear-floor monotonic edge (#7), respected.

Minor text nit: tires §1.2 example 2 rounds Front correctly but Rear `30.7−8=22.7→22.5`, while the spec text shows `23.0`. Use 22.5 or adjust the prose.