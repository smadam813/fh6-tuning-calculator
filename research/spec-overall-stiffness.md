# Overall Stiffness Slider — Calculation Spec

> **Provenance / honesty note.** Like the handling-bias dial, this is **pure
> calculator synthesis**, not a transcription of any single published guide. No
> source in `findings.json` exposes a unified "overall stiffness" dial. What the
> sources *do* establish — and what this slider composes — is the well-understood
> firmness levers every Forza tuner moves together when they want a harder or
> softer car:
>
> - **Springs:** stiffer springs (higher rate / higher ride frequency) flatten the
>   platform and sharpen response; softer springs add mechanical grip and
>   compliance. (spec-springs-damping §1)
> - **Anti-roll bars:** stiffer bars cut body roll and quicken turn-in; softer
>   bars let the car lean and keep more rubber loaded over bumps. (spec-alignment-arb §2)
> - **Damping:** firmer bump/rebound tightens body control; softer damping is more
>   absorbent. (spec-springs-damping §3)
> - **Ride height:** the goal data ties stiffness to height — the stiff tarmac
>   goals (Circuit/Touge) sit at the ride-height **floor**, the soft goals
>   (Off-Road/Rally) sit **tall** (spec-springs-damping §2.2). The engine's own
>   anti-bottoming guard *raises* ride height when springs go soft
>   (spec-springs-damping §2.3). So "harder → lower, softer → higher" is the
>   direction the research already encodes.
>
> This file documents how the calculator **bundles those four firmness levers into
> one −5…+5 dial** so a user can make the whole car harder or softer with a single
> control, while every lever still obeys its researched direction and legal range.

---

## 1. Rationale — the *magnitude* companion to Handling Bias

Handling Bias and Overall Stiffness are deliberately **orthogonal**:

| Dial | What it changes | How it moves the two ends |
|---|---|---|
| **Handling Bias** | front/rear **balance** (a *ratio* knob) | each end the **opposite** way (soften front bar / stiffen rear, …) |
| **Overall Stiffness** | suspension **firmness** (a *magnitude* knob) | both ends the **same** way (stiffen front *and* rear together) |

Because stiffness scales both ends by the same factor, the front/rear ratio is
preserved (bar a small asymmetry when one end is already clamped at a range
boundary) — so **stiffness leaves the car's handling balance essentially
unchanged**, and the two dials compose cleanly: set how hard the platform is with
one, shift the balance with the other.

- **+stiff → Hard:** raise spring rate, anti-roll bars and damping; drop ride
  height toward the floor. A firmer, lower, flatter car.
- **−stiff → Soft:** the mirror — softer springs/bars/dampers, taller ride height.
  A more compliant car (and the extra ride height gives the softer springs room
  not to bottom).
- **stiff = 0:** the slider is a **no-op**. `compute()` skips
  `applyOverallStiffness` when `stiff === 0`, so every baseline value is returned
  byte-for-byte identical — the same hard contract as bias, verified by the Node
  sweep.

The dial is applied **as a post-process on top of the fully-computed per-goal
tune**. Stiffness runs **before** handling bias in `compute()` (set the firmness,
then shift the balance); because both are multiplicative the order only matters
at the clamps.

---

## 2. Non-linear response curve

Stiffness reuses the shared signed power curve so the dial is gentle near center
and firmer toward the extremes:

```
biasScale(s, exp) = sign(s) · (min(|s|, 5) / 5) ^ exp     →   −1 … +1
```

- At `s = 0` the scale is `0` (no change — reinforces the stiff-0 neutrality).
- At `s = ±5` the scale is `±1` (full per-lever magnitude).

---

## 3. Per-lever magnitudes and exponents

Every lever moves **both ends by the same factor** (balance-preserving).

| Lever | Magnitude at ±5 | Exponent | Why |
|---|---|---|---|
| **Springs** front & rear | ±25 % each | **1.1** | ±25 % rate ≈ ±12 % ride frequency — roughly one class tier (Sports↔HighPerf↔Race in spec-springs-damping §1.2) of firmness, the natural span of "a bit softer / a bit harder". Near-linear so the platform firms up progressively. |
| **Anti-roll bars** front & rear | ±30 % each | **1.15** | Bars are the coarse, fast roll-stiffness tool, so they get the largest swing; the slightly higher exponent keeps small dial moves calm before ramping up at the extremes. |
| **Damping** bump + rebound (all 4) | ±25 % | **1.1** | Bump and rebound are scaled by the **same** factor so their ~60 % bump:rebound ratio (spec-springs-damping §3.1) is held (to within the 0.1 damping step); near-linear to track the spring change. |
| **Ride height** front & rear | ±15 % of the part's travel | **1.1** | Harder → lower toward the floor, softer → taller. Modest because ride height is primarily a CoG/clearance axis; this is the *secondary* stiffness correlation, not the headline. Both ends move together, and the clamp naturally no-ops the direction with no headroom (e.g. a Circuit tune already at the floor can't drop further, but can still rise when softened). |

All outputs are re-clamped to their legal Forza ranges after the delta: springs
within the part min/max, ARB 1–65, damping 1–20, ride height within the part
min/max.

---

## 4. Exemptions (intentional)

- **Stock suspension — the whole dial is a no-op.** Stock suspension locks
  springs, bars, dampers and ride height at the factory in-game (`canTuneSusp`
  is false), so there is nothing for the firmness dial to move. The post-process
  returns the tune untouched, mirroring how alignment/ARB already behave on stock.
- **Balance levers are never touched.** Differential, brake balance, aero,
  alignment and tire pressure are *balance/grip* levers, not firmness — stiffness
  leaves them exactly as the goal (and the bias dial) set them. This is what keeps
  the two dials orthogonal, and is asserted directly in the integration tests.

Each affected card's "Why & formula" text gets a one-line *Overall stiffness:*
note so the effect is never silent, and the app's **"What the sliders changed"**
panel lists every value the dial moved (neutral → current) with a plain-language
effect.

---

## 5. Worked example

**Car:** RWD front-engine ICE, A-class, 450 hp, 3300 lb, 52 % front, Sport tires,
Race suspension, full aero, goal = **Circuit**. Baseline (stiff 0):
ARB F 19 / R 17, springs F 728 / R 554 lb/in, ride F 4.5 / R 4.6 in,
rebound F 9.3, bump F 5.6.

**stiff = +4 (toward Hard), scale = (4/5)^exp:**

- Springs exp 1.1 → `s = 0.8^1.1 ≈ 0.782`. F ×(1 + 0.25·0.782) ≈ ×1.196 →
  728 → **870**; R 554 → **662** (ratio 728/554 ≈ 870/662 ≈ 1.31, balance held).
- ARB exp 1.15 → `s = 0.8^1.15 ≈ 0.774`. ×(1 + 0.30·0.774) ≈ ×1.232 → F 19 → **23**, R 17 → **21**.
- Damping exp 1.1 → ×1.196 → rebound F 9.3 → **11.1**, bump F 5.6 → **6.7** (ratio held to within 0.1-step rounding).
- Ride exp 1.1 → drop 0.15·travel·0.783. Front is already at the 4.5 floor → stays
  **4.5**; rear 4.6 → **4.5**.

Net: a firmer, lower, flatter Circuit car with the **same** front/rear balance —
every value still inside its legal slider range. **stiff = −4** produces the
mirror (softer springs/bars/dampers, taller ride). **stiff = 0** changes nothing.

---

## 6. Clamps / contract summary

- `biasScale(s, exp) = sign(s)·(min(|s|,5)/5)^exp`, range −1…+1.
- stiff = 0 ⇒ post-process not invoked ⇒ baseline returned unchanged (hard contract).
- Both ends of every lever scaled by the **same** factor ⇒ front/rear ratio
  (handling balance) preserved — except for a small asymmetry when a lever is
  already clamped at a range boundary (e.g. a spring near the part max).
- Balance levers (diff, brakes, aero, alignment, tires) are never touched.
- Stock suspension exempts the entire dial.
- Every lever re-clamped to its legal range after the delta.
- Composes with Handling Bias; both can run at once and the result stays in range
  (verified across the full bias × stiffness grid in `sweep.js`).
