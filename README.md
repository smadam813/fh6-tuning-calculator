# FH6 Tuning Calculator

A single-page web app that turns a car's known stats into a complete, math-derived
Forza Horizon 6 tune across **every** tuning category — with the formula and reasoning
shown beneath each output, and live side-by-side comparison of all six tuning goals.

Open it with **no server and no build step**: just open `index.html` in any modern
browser (works from `file://`, so you can keep it on a phone or second monitor while
in-game).

```
index.html      structure + inputs/outputs
styles.css      dark, mobile-first responsive theme
tuning.js       the engine — pure compute(input, goal) -> tune
app.js          UI: live binding, units, compare table, copy
research/        the sourced formulas this engine is built on (provenance)
```

## What it does

**Inputs** — drivetrain, engine location, powertrain, PI class, power, torque, weight,
front-weight %, gear count, tire compound, suspension type, aero, and the ride-height /
spring-rate ranges read off the car's installed parts. Imperial **or** metric.

**Outputs** (per goal, or all goals side-by-side):
Tires (F/R psi) · Gearing (final drive + every gear ratio) · Alignment (camber F/R,
toe F/R, caster) · Anti-roll bars (F/R) · Springs (F/R rate + ride height F/R) ·
Damping (rebound F/R, bump F/R) · Aero (F/R downforce) · Braking (balance, pressure) ·
Differential (accel/decel lock, per driven axle + AWD center split).

Every value is a concrete number, clamped to the legal Forza slider range, and adapts
meaningfully across the six **goals**: Circuit · Drag · Drift · Off-Road · Rally · Touge.

## The formulas (and why)

These are the community-canonical Forza tuning formulas (consistent across FH4/FH5/FH6),
cross-referenced across multiple guides and reconciled by an adversarial review pass.
Full derivations, per-goal modifier tables and edge cases live in [`research/`](research/).

| Category | Core formula |
|---|---|
| **Springs** | Ride-frequency model: `K = f² · W_corner / 9.78`, where `f` (Hz) is a target ride frequency by class + aero + goal, and `W_corner` is the static load on that corner. Clamped to the part's lb/in range, with a support-floor so the car can't bottom out. |
| **Ride height** | Fraction of the part's min↔max range per goal (Circuit = min for low CoG, Off-Road = max, Drag = forward rake), plus compound/aero/anti-bottoming nudges. |
| **Damping** | `Bump = MinBump(class) + frontAxle/200 × 0.1`; `Rebound = Bump / 0.6`. Rear offset by front/rear spring-rate gap; per-goal stiffness multipliers; bump held to 40–70% of rebound (bypassed for off-road/rally/drag-launch). |
| **Gearing** | `FinalDrive = 4.25 + (400 − hp)/600` (+ weight, goal, aero, drivetrain, engine-location terms). Gear ratios follow the power series `Rₙ = A · nᴮ` with first gear `A` from power-to-weight and spacing exponent `B ≈ −0.65` (wider for off-road, tighter for drag). EVs are single-speed. |
| **Alignment** | Front camber from a tire-grip factor (−0.6° rally rubber → −2.0° slicks), shifted by drivetrain and front load; rear ≈ 55% of front. Caster scales with weight + PI class. Toe near zero except goal-driven toe-out (drift/turn-in) and rear toe-in (RWD stability). |
| **Anti-roll bars** | ForzaFire base `(weight/2) / (200 − 200 · classStiffness%)`, split front/rear by the weight-distribution deviation (RWD ±1 / AWD ±0.66 / FWD ∓1 per 1%), then per-goal multipliers (e.g. drift = soft front / very stiff rear). |
| **Aero** | Overall downforce *level* per goal × an aero-*balance* (front share) per drivetrain, expressed as % of each wing's slider range. AWD circuit forces max-front/min-rear; drag floors both. **Single-wing cars** (front-splitter-only or rear-wing-only) can't be rebalanced, so the one wing you have is sized to the car's existing tendency — e.g. a rear-wing-only car that already understeers keeps the wing low instead of maxing it. |
| **Braking** | Balance = drivetrain base (RWD 52 / AWD 54 / FWD 58) + ½% per 1% front weight + goal. Pressure = 100 + mass + compound + goal. |
| **Differential** | Per-axle base + a power-to-weight trim (high power loosens accel lock) + per-goal adjust. FWD front-only (accel capped 95), RWD rear-only, AWD all three with a center split that's floored at 50% rear. |

Each card in the app shows its own formula and a one-line rationale under **“Why & formula.”**

## Edge cases handled

FWD vs RWD vs AWD (diff routing, ARB/camber/brake bias inversion) · EV (single-speed,
heavier-body spring/damper trims, regen brake bias by drive axle) vs ICE vs Hybrid ·
front/mid/rear engine weight bias · aero kit (full / front-splitter-only / rear-wing-only /
none — single-wing setups sized to the car's balance tendency since you can't offset with
the missing end) · tire-compound grip · stock suspension (alignment/ARB locked) ·
soft-by-design goals kept soft instead of slammed to max.

## Sources

forza.guide · ForzaTune · ForzaFire · grindout · skycoach · vpesports · gamingpromax ·
Nexus Mods · plus community build-theory threads and tuning spreadsheets. There is no
official Forza tuning formula — all math is community-standard, validated through testing,
and these values are **starting points**: fine-tune from there with in-game telemetry.

## Verification

`tuning.js` was validated across 2,400+ input combinations (every drivetrain × powertrain ×
goal × representative class/compound/suspension/power/weight): zero crashes, every output
within its legal slider range, gear ratios strictly descending, all six goals producing
distinct tunes.
