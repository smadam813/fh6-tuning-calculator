# Handling Bias Slider — Calculation Spec

> **Provenance / honesty note.** This is **pure calculator synthesis**, not a
> transcription of any single published community guide. No source in
> `findings.json` (ForzaFire, ForzaTune, grindout, forza.guide, gamingpromax,
> SkyCoach, et al.) exposes a unified "handling bias" dial with these exact
> exponents and magnitudes. What the sources *do* establish — and what this
> slider composes — is the per-lever cause/effect that every Forza tuner uses
> manually:
>
> - **Anti-roll bars:** softer front / stiffer rear → more rotation (oversteer);
>   stiffer front / softer rear → more front plant (understeer). (spec-alignment-arb §2)
> - **Springs:** a rear-biased spring frequency frees the rear; a front-biased
>   one plants it. (spec-springs-damping §1)
> - **Differential:** more accel/decel lock and more rear torque (AWD center) →
>   tighter, more rearward drive → oversteer-leaning; less → understeer-leaning.
>   (spec-aero-diff §2)
> - **Brake balance:** forward bias loads the front first on entry; rearward bias
>   eases the rear. (spec-tires-braking §2.A)
> - **Aero:** shift the front/rear downforce balance — raise front-share for a looser rear at speed
>   (oversteer), lower it to plant the rear (understeer). (spec-aero-diff §1.1)
>
> This file documents how the calculator **bundles those five known levers into
> one −5…+5 dial** so a user can shift the whole car's balance with a single
> control, while every individual lever still obeys its researched direction and
> legal range.

---

## 1. Rationale

A tuner who wants "a little less understeer" does not touch one setting — they
nudge several at once, in the same direction, each by a sensible amount. The
handling-bias slider automates that coordinated nudge:

- **+bias → oversteer:** free the rear and plant the front (soften front bar,
  stiffen rear bar; soften front springs, stiffen rear; raise diff lock / send
  torque rearward; bias brakes forward; raise front aero / drop rear).
- **−bias → understeer:** the mirror image (plant the rear, free the front).
- **bias = 0:** the slider is a **no-op**. The post-process is not even invoked
  (`compute()` skips `applyHandlingBias` when `bias === 0`), so every baseline
  value is returned byte-for-byte identical. This is a hard contract verified by
  the Node sweep.

The dial is applied **as a post-process on top of the fully-computed per-goal
tune**, so it never replaces a goal's character — it only shifts balance around
the goal's baseline by deltas that are small relative to the per-goal spread.

---

## 2. Non-linear response curve

Each lever uses a signed power curve so the dial is gentle near center and
firmer toward the extremes:

```
biasScale(b, exp) = sign(b) · (min(|b|, 5) / 5) ^ exp     →   −1 … +1
```

- At `b = 0` the scale is `0` (no change — reinforces the bias-0 neutrality).
- At `b = ±5` the scale is `±1` (full per-lever magnitude).
- A **higher exponent** keeps the lever quieter in the mid-range and only "wakes
  up" near the ends; a **lower exponent** is closer to linear.

---

## 3. Per-lever magnitudes and exponents

| Lever | Magnitude at ±5 | Exponent | Why this exponent |
|---|---|---|---|
| **ARB** front/rear | ±8 % each end | **1.2** | Bars are a coarse, fast balance tool; a gentle mid-range (exp > 1) avoids over-rotating the car on small dial moves, then ramps up at the extremes where the user clearly wants a big balance change. |
| **Springs** front | ±12 % | **1.1** | Springs change frequency/grip distribution more subtly than bars; near-linear (exp just above 1) so the front loads up progressively. Front gets the larger swing (±12 %) because front frequency dominates turn-in balance. |
| **Springs** rear | ±4 % | **1.1** | Smaller rear swing keeps rear ride quality/traction intact while still shifting the front/rear frequency ratio. |
| **Differential** accel | ±12 pts | **1.4** | Diff lock has a strong, sometimes snappy effect on exit balance; the **highest** exponent keeps it nearly inert in the mid-range (so casual ±1–2 dialing does not make the car violent on throttle) and reserves the aggressive change for the ±4–5 extremes. |
| **Differential** decel | ±8 pts | **1.2** | Off-throttle entry balance; slightly tamer than accel because lift-off effects are easier to provoke. |
| **AWD center** (rear torque %) | ±6 pts | **1.1** | Center split is a smooth, almost linear handling lever — a near-linear curve (exp 1.1) makes ±5 on center feel proportional and predictable, unlike the snappier accel lock (exp 1.4). Routing torque rearward gradually sharpens an AWD car without the on/off feel a steep curve would give. |
| **Brake balance** | ±4 pts | **1.0 (linear)** | Brake bias is already a small, intuitive, single-number lever; a plain linear response is the least surprising. |
| **Aero** front/rear | ±0.08 front-share | **1.05** | Shifts the aero BALANCE (front's share of total downforce), preserving total, solved in lbf so the effect is identical across kits. +bias raises front-share (toward oversteer); −bias lowers it. Single-wing cars nudge the lone end ±8% of its range. |

All outputs are re-clamped to their legal Forza ranges after the bias delta:
ARB 1–65, springs within part min/max, diff accel/decel 0–100 (FWD accel ≤95,
FWD decel ≥5), AWD center 50–90, brake balance 40–65, aero % 0–100.

---

## 4. Goal exemptions (intentional)

- **Drift — brake balance is NOT moved.** Drift locks brake balance at a fixed
  48 % for maximum rotation; the slider deliberately leaves it there. Users tune
  a drift car's balance with the *other* levers (ARB, springs, aero, and the AWD
  center diff).
- **Drift — accel/decel diff lock is NOT moved.** The near-welded drive axle
  (accel override 100 / high decel) is core drift character, so the dial leaves
  the RWD/FWD lock and the AWD rear lock untouched. (The AWD **center** split is
  still allowed to move, because shifting torque fore/aft changes an AWD drift
  car's balance without breaking the welded-axle feel.)
- **Drag — aero is NOT moved.** Drag floors downforce to zero for top speed;
  there is nothing to bias.

These exemptions are surfaced to the user via the UI hint and the affected
card's "Why & formula" text so the silent non-effect is never a surprise.

---

## 5. Worked example

**Car:** RWD, front-engine, ICE, A-class, 450 hp, 3000 lb, 52 % front, Sport
tires, Race suspension, full aero kit (front 30–165 lbf, rear 50–300 lbf),
goal = **Circuit**. Baseline (bias 0) from the engine:
ARB F ≈ 19 / R ≈ 17, springs F/R from the frequency model, diff accel 52 % /
decel 20 %, brake balance 57 %, aero F 85 % / R 85 %.

**bias = +3 (toward oversteer), scale = (3/5)^exp:**

- ARB exp 1.2 → `s = 0.6^1.2 ≈ 0.542`. Front ×(1 − 0.08·0.542) ≈ ×0.957,
  rear ×(1 + 0.08·0.542) ≈ ×1.043 → softer front, stiffer rear.
- Diff accel exp 1.4 → `s = 0.6^1.4 ≈ 0.487`. accel +12·0.487 ≈ +5.8 → ~58 %
  (snapped even); decel exp 1.2 → +8·0.487·… → a few points up.
- Brake balance linear → +4·(3/5) = +2.4 → 59 % (forward bias).
- Aero exp 1.05 → `s ≈ 0.583`. front +8·0.583 ≈ +4.7 → 90 %; rear −4.7 → 80 %.

Net: the car rotates more on entry, drives tighter on exit, and is looser at the
rear at speed — a coherent shift toward oversteer, with every value still inside
its legal slider range and still recognizably a Circuit tune.

**bias = −3** produces the exact mirror (planted rear, freer front → understeer
reduction of an already-loose car / added stability).

**bias = 0** changes nothing — the post-process is skipped entirely.

---

## 6. Clamps / contract summary

- `biasScale(b, exp) = sign(b)·(min(|b|,5)/5)^exp`, range −1…+1.
- bias = 0 ⇒ post-process not invoked ⇒ baseline returned unchanged (hard contract).
- Every lever re-clamped to its legal range after the delta.
- Drift exempts brake balance and accel/decel diff lock; Drag exempts aero.
- Deltas are small relative to per-goal spread, so goal and drivetrain
  distinctness are preserved at every bias value.
