# Aero Balanced-Magnitude Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the buggy "AWD max-front/min-rear" aero logic with a weight-anchored balanced-magnitude model (front and rear target equal downforce at a 47% front-weight ideal, ±1.867 lbf rear per 1% deviation), implemented C#-first and validated by C#-native tests, with aero carved out of the JS parity gate as the first step of the planned parity-layer deprecation.

**Architecture:** `Fh6Tuning.Core.TuningEngine.Aero(...)` full-kit branch is rewritten to solve front/rear downforce in **lbf magnitude** space (not slider-% or drivetrain slider rules). The handling-bias dial's aero lever is reworked to shift the **aero balance (front-share)** by a bounded amount, kit-independent. The parity harness exempts aero numeric + why leaves (it is now C#-validated). The legacy JS oracle is **not** updated (Option B — aero is C#-canonical going forward).

**Tech Stack:** .NET 10, C#, xUnit. All engine math is `double` via `JsMath` rounding (`R5`, `JsMath.Round`, `Clamp`). No Node/JS changes.

**Decisions locked in (from brainstorming + deep-research):**
- Balance target = weight distribution, anchored at 47% front = balanced (front ≈ rear in lbf). Coefficient 1.867 lbf/% verified (QuickTune).
- Drivetrain does NOT set the aero balance (the "AWD max front/min rear" rule was a kit artifact; the drivetrain ratio targets were refuted in verification). **Pure weight model, RWD/FWD included.**
- **Rear-engine safeguard:** never below 0.50 front-share (rear lbf ≥ front lbf).
- Goal **LEVEL** (Circuit 0.85, etc.) is unchanged — it sets total downforce (grip vs top speed), orthogonal to balance.
- Work in **lbf magnitude**, never the in-game Aero Balance stat (its direction is unverified). Display lbf + slider %.
- **Option B:** aero is exempt from the parity numeric/why gate; legacy JS aero is left stale-but-harmless.

**Reference (verified deltas, current → proposed):** user car AWD/Mid/47%/big-wing Circuit `0.69 → 0.50`; full-kit sweep: 41/45 cases change, **0** proposed shares outside 0.33–0.67.

---

## File Structure

- **Modify** `Fh6Tuning.Core/TuningEngine.cs`
  - Full-kit aero branch (currently ~lines 702–743): replace the `bal`/`shift`/override/engine-shed/why block with the balanced-magnitude model.
  - `ApplyHandlingBias` aero block (currently ~lines 1006–1033): replace with balance-space shift (full kit) + per-end nudge (single wing).
- **Modify** `Fh6Tuning.Tests/ParityHarness.cs` — exempt aero numeric + aero why leaves from the gate (`IsAeroExempt`).
- **Create** `Fh6Tuning.Tests/AeroModelTests.cs` — C#-native aero validation (regression, balanced-at-47, direction, rear-engine safeguard, front-stays-high, band invariant, dial kit-independence + direction).
- **Modify** `research/spec-aero-diff.md` — correct the AERO section to the balanced-magnitude model.
- **Modify** `research/spec-handling-bias.md` — correct the aero lever description (balance-space, ±0.08 share).
- **Modify** `CLAUDE.md` — note aero is C#-canonical / exempt from parity (deprecation pilot).
- **Delete** `aero-verify.cjs` (throwaway grid script, repo root).

Unchanged (do NOT touch): the splitter-only and rear-only aero branches (single-wing logic is not the bug); all non-aero categories; `legacy/tuning.js` and `legacy/parity-export.js`.

---

## Task 1: Carve aero out of the parity gate (deprecation pilot)

Do this FIRST so later aero changes don't break parity. Behavior-preserving now (C# aero still matches JS, so the exempted leaves were passing anyway) — keeps the suite green at every later step.

**Files:**
- Modify: `Fh6Tuning.Tests/ParityHarness.cs`

- [ ] **Step 1: Add the `IsAeroExempt` predicate**

In `ParityHarness.cs`, next to `IsWhyString` (around line 254), add:

```csharp
    // Aero is validated by C#-native AeroModelTests (the balanced-magnitude model intentionally
    // diverges from the legacy JS oracle). Its numeric magnitudes (front/frontLbf/rear/rearLbf) and
    // why.* prose are EXEMPT from the JS parity gate — the first carve-out of the planned parity-layer
    // deprecation. aero.applicable (a bool shape leaf) stays gated; the new model preserves it.
    private static bool IsAeroExempt(string path) =>
        path.StartsWith("aero.", StringComparison.Ordinal) &&
        !path.EndsWith(".applicable", StringComparison.Ordinal);
```

- [ ] **Step 2: Skip aero numeric leaves in the forward walk**

In `Walk`, the numeric-leaf branch begins with `if (TryGetNumber(expVal, out double expNum))` (around line 166). Add an exemption as the first line inside that block:

```csharp
                if (TryGetNumber(expVal, out double expNum))
                {
                    if (IsAeroExempt(path)) break; // aero numerics are C#-validated, not JS-gated
                    // numeric leaf — the hard parity gate
                    double expN = Norm(expNum);
```

- [ ] **Step 3: Skip aero why strings in the forward walk**

In the same `Walk`, the string-leaf branch has `if (IsWhyString(path) || IsSummaryString(path))` (around line 195). Change it to exclude aero:

```csharp
                    if ((IsWhyString(path) || IsSummaryString(path)) && !IsAeroExempt(path))
```

- [ ] **Step 4: Run the full test suite — expect GREEN**

Run: `dotnet test Fh6Tuning.sln`
Expected: PASS. Aero leaves are now skipped; since C# aero currently still matches JS, nothing else changes. `ParityTests.NumericParity`, `HardParity_Aggregate`, `WhyStringParity_Soft`, and `SweepTests` all green.

- [ ] **Step 5: Commit**

```bash
git add Fh6Tuning.Tests/ParityHarness.cs
git commit -m "test(parity): exempt aero from JS parity gate (deprecation pilot)"
```

---

## Task 2: Rewrite the full-kit aero baseline (balanced-magnitude model)

**Files:**
- Create: `Fh6Tuning.Tests/AeroModelTests.cs`
- Modify: `Fh6Tuning.Core/TuningEngine.cs` (full-kit aero branch)

- [ ] **Step 1: Write the failing native aero tests**

Create `Fh6Tuning.Tests/AeroModelTests.cs`:

```csharp
using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// C#-native validation of the balanced-magnitude aero model (replaces JS parity for aero).
/// Encodes the model's INTENT: front and rear target equal downforce at 47% front weight; rear rises
/// +1.867 lbf per 1% front-weight above 47%; rear-engine never below balanced; front stays high on
/// Circuit; aero balance never pathological for representative kits; the handling-bias aero lever
/// shifts BALANCE kit-independently.
/// </summary>
public sealed class AeroModelTests
{
    private static readonly ITuningEngine Engine = new TuningEngine();

    // Representative kits: rear range ≥ front range (the realistic case). STD ≈ standard Forza kit
    // (front_max ≈ rear_min), BIG ≈ a WTAC-style big rear wing.
    private static readonly AeroRange StdF = new(30, 165), StdR = new(50, 300);
    private static readonly AeroRange BigF = new(235, 704), BigR = new(240, 1038);

    private static TuneInput Car(Drivetrain dt, EngineLocation el, double fwp, double power,
        AeroRange fr, AeroRange rr) => Fixtures.BaseInput(b =>
    {
        b.Drivetrain = dt; b.EngineLocation = el; b.FrontWeightPct = fwp; b.Power = power;
        b.PiClass = PiClass.S2; b.Weight = 3000; b.TireCompound = TireCompound.Race;
        b.SuspensionType = SuspensionType.Race;
        b.HasFrontAero = true; b.HasRearAero = true; b.AeroInstalled = true;
        b.AeroFront = fr; b.AeroRear = rr;
    });

    private static double Share(Tune t) =>
        t.Aero.FrontLbf!.Value / (t.Aero.FrontLbf!.Value + t.Aero.RearLbf!.Value);

    [Fact] // Regression: the exact failing case. Was 100/10 (share 0.69).
    public void UserCar_Circuit_IsBalanced_FrontStaysHigh()
    {
        var car = Car(Drivetrain.AWD, EngineLocation.Mid, 47, 630, BigF, BigR);
        Tune t = Engine.Compute(car, Goal.Circuit);
        Assert.InRange(Share(t), 0.45, 0.55);              // balanced, not 0.69
        Assert.True(t.Aero.Front!.Value >= 85, $"front collapsed to {t.Aero.Front}");
    }

    [Fact] // At 47% front, front and rear target equal downforce.
    public void BalancedAt47_FrontApproxRearLbf()
    {
        foreach (var dt in new[] { Drivetrain.FWD, Drivetrain.RWD, Drivetrain.AWD })
        {
            Tune t = Engine.Compute(Car(dt, EngineLocation.Front, 47, 450, BigF, BigR), Goal.Circuit);
            Assert.True(Math.Abs(t.Aero.FrontLbf!.Value - t.Aero.RearLbf!.Value) <= 2,
                $"{dt}: front {t.Aero.FrontLbf} vs rear {t.Aero.RearLbf} not balanced at 47%");
        }
    }

    [Fact] // Front-heavy → more rear downforce (the verified 1.867 lb/% direction).
    public void RearDownforce_RisesWithFrontWeight()
    {
        Tune light = Engine.Compute(Car(Drivetrain.AWD, EngineLocation.Front, 47, 450, BigF, BigR), Goal.Circuit);
        Tune heavy = Engine.Compute(Car(Drivetrain.AWD, EngineLocation.Front, 57, 450, BigF, BigR), Goal.Circuit);
        Assert.True(heavy.Aero.RearLbf!.Value > light.Aero.RearLbf!.Value,
            $"57%-front rear {heavy.Aero.RearLbf} not > 47%-front rear {light.Aero.RearLbf}");
    }

    [Fact] // Rear-engine safeguard: never below balanced (rear ≥ front).
    public void RearEngine_NeverBelowBalanced()
    {
        Tune t = Engine.Compute(Car(Drivetrain.RWD, EngineLocation.Rear, 38, 450, BigF, BigR), Goal.Circuit);
        Assert.True(t.Aero.RearLbf!.Value >= t.Aero.FrontLbf!.Value - 1,
            $"rear-engine front-biased: front {t.Aero.FrontLbf} > rear {t.Aero.RearLbf}");
        Assert.InRange(Share(t), 0.40, 0.51);
    }

    [Fact] // No pathological aero split anywhere on representative kits.
    public void AllFullKitCases_ShareInBand()
    {
        var dts = new[] { Drivetrain.FWD, Drivetrain.RWD, Drivetrain.AWD };
        var els = new[] { EngineLocation.Front, EngineLocation.Mid, EngineLocation.Rear };
        var fwps = new double[] { 42, 50, 57 };
        var kits = new[] { (StdF, StdR), (BigF, BigR) };
        var goals = new[] { Goal.Circuit, Goal.Touge, Goal.Rally, Goal.OffRoad, Goal.Drift };
        foreach (var dt in dts)
            foreach (var el in els)
                foreach (var fwp in fwps)
                    foreach (var (fr, rr) in kits)
                        foreach (var g in goals)
                        {
                            Tune t = Engine.Compute(Car(dt, el, fwp, 450, fr, rr), g);
                            double sh = Share(t);
                            Assert.True(sh >= 0.33 && sh <= 0.67,
                                $"{dt}/{el} {fwp}% {g}: aero share {sh:0.00} outside 0.33..0.67");
                        }
    }

    [Fact] // Circuit keeps front downforce high (does not collapse).
    public void Circuit_FrontStaysHigh()
    {
        foreach (var dt in new[] { Drivetrain.FWD, Drivetrain.RWD, Drivetrain.AWD })
        {
            Tune t = Engine.Compute(Car(dt, EngineLocation.Front, 50, 450, BigF, BigR), Goal.Circuit);
            Assert.True(t.Aero.Front!.Value >= 80, $"{dt}: Circuit front {t.Aero.Front} too low");
        }
    }
}
```

- [ ] **Step 2: Run the new tests to verify they FAIL**

Run: `dotnet test Fh6Tuning.sln --filter "FullyQualifiedName~AeroModelTests"`
Expected: FAIL. `UserCar_Circuit_IsBalanced_FrontStaysHigh` fails (current share ≈ 0.69); `BalancedAt47_FrontApproxRearLbf`, `AllFullKitCases_ShareInBand`, etc. fail against the current override.

- [ ] **Step 3: Replace the full-kit aero branch with the balanced-magnitude model**

In `Fh6Tuning.Core/TuningEngine.cs`, replace the full-kit block. The block to replace **starts** at the comment `// full kit (both ends) — solve for a target front/rear balance share` (currently line 702) and **ends** at `return new Aero(true, fp2, frontLbf, rp2, rearLbf, new Why(text, formula));` (currently line 743). Replace that entire span with:

```csharp
        // full kit (both ends) — BALANCED-MAGNITUDE model anchored at a 47% front-weight ideal.
        // Aero balance comes from WEIGHT DISTRIBUTION (QuickTune-canonical), not a drivetrain slider
        // rule: front sits at the goal LEVEL of its range; rear targets the SAME downforce MAGNITUDE
        // (balanced at 47%), trimmed +1.867 lbf per 1% of front-weight above 47%. No AWD slider
        // override, no engine-location rear-shed; aero no longer reads the over/understeer flags.
        // Rear-engine cars are safeguarded: never below 0.50 front-share (rear lbf ≥ front lbf).
        double front, rear; // slider fractions 0..1
        if (goal == Goal.Drag)
        {
            front = 0; rear = 0;
        }
        else if (fR.HasRange && rR.HasRange)
        {
            double fSpan = fR.Max!.Value - fR.Min!.Value;
            double rSpan = rR.Max!.Value - rR.Min!.Value;
            double frontDF = fR.Min.Value + fSpan * level;
            if (i.Power >= 600 && goal != Goal.OffRoad)
                frontDF *= 1 + Math.Min((i.Power - 600) / 600, 0.5) * 0.5;
            frontDF = Clamp(frontDF, fR.Min.Value, fR.Max.Value);          // clamp front first
            double rearDF = frontDF + (i.FrontWeightPct - 47) * 1.867;     // balance to (clamped) front
            if (i.EngineLocation == EngineLocation.Rear) rearDF = Math.Max(rearDF, frontDF); // safeguard
            rearDF = Clamp(rearDF, rR.Min.Value, rR.Max.Value);
            front = fSpan > 0 ? (frontDF - fR.Min.Value) / fSpan : 0;
            rear = rSpan > 0 ? (rearDF - rR.Min.Value) / rSpan : 0;
        }
        else
        {
            // ranges unknown: fraction-space balance (front ≈ rear fraction), weight-trim via 250-lbf span.
            double frontF = level, rearF = level;
            if (i.Power >= 600 && goal != Goal.OffRoad)
            {
                double k = 1 + Math.Min((i.Power - 600) / 600, 0.5) * 0.5;
                frontF *= k; rearF *= k;
            }
            rearF += (i.FrontWeightPct - 47) * 1.867 / 250;
            if (i.EngineLocation == EngineLocation.Rear) rearF = Math.Max(rearF, frontF);
            front = Clamp(frontF, 0, 1);
            rear = Clamp(rearF, 0, 1);
        }

        double fp2 = R5(front * 100), rp2 = R5(rear * 100);
        double? frontLbf = ToLbf(front, fR);
        double? rearLbf = ToLbf(rear, rR);
        // share: front's portion of total ACTUAL downforce when lbf known, else from %.
        double share = (frontLbf != null && rearLbf != null && frontLbf + rearLbf > 0)
            ? JsMath.Round(frontLbf.Value / (frontLbf.Value + rearLbf.Value) * 100)
            : (fp2 + rp2 > 0 ? JsMath.Round(fp2 / (fp2 + rp2) * 100) : 50);

        string text =
            $"Downforce trades top speed for grip. {GoalName(goal)} runs {S(fp2)}% front / {S(rp2)}% of each wing's range (aero balance ≈ {S(share)}% front)" +
            (goal == Goal.Circuit ? ", near-max grip with rear sized to match front downforce so the car stays balanced at speed"
             : goal == Goal.Drag ? ", floored to zero — every pound of downforce is drag that kills top speed."
             : goal == Goal.Drift ? ", low so the car stays loose and easy to swing."
             : ", a moderate amount for the surface and speed.")
            + (goal != Goal.Drag && i.FrontWeightPct > 47 ? " Nose-heavy, so rear downforce is raised to keep balance." : "")
            + (goal != Goal.Drag && i.FrontWeightPct < 47 ? " Rear-weight bias, so rear downforce eases off the balanced point." : "")
            + (goal != Goal.Drag && i.EngineLocation == EngineLocation.Rear ? " Rear-engine: rear downforce held at least even with the front." : "")
            + ((HasRange(fR) || HasRange(rR)) ? lbfNote : "");
        string formula =
            $"front = level({S(level)}) × frontRange\nrear  = frontLbf + (frontWeight − 47) × 1.867   (balanced at 47%)" + ((HasRange(fR) || HasRange(rR)) ? lbfFormula : "");

        return new Aero(true, fp2, frontLbf, rp2, rearLbf, new Why(text, formula));
```

Note: this removes the only remaining use of the local `oversteerProne`/`understeerProne` in the full-kit branch. They are still used by the splitter-only and rear-only branches above (line 673 onward) — leave those declarations and branches untouched.

- [ ] **Step 4: Run the new aero tests — expect PASS**

Run: `dotnet test Fh6Tuning.sln --filter "FullyQualifiedName~AeroModelTests"`
Expected: PASS (all six tests).

- [ ] **Step 5: Run the full suite — expect GREEN**

Run: `dotnet test Fh6Tuning.sln`
Expected: PASS. Parity hard gate green (aero exempt); `SweepTests` green (aero still in [0,100], goals distinct, drivetrains distinct via ARB/diff/etc., bias-0 neutrality holds). If `SweepTests.VaryingOnlyDrivetrain_ProducesDistinctTunes` fails, STOP — it would mean aero was the only drivetrain differentiator for some config (it should not be; ARB/diff/braking still vary by drivetrain).

- [ ] **Step 6: Commit**

```bash
git add Fh6Tuning.Core/TuningEngine.cs Fh6Tuning.Tests/AeroModelTests.cs
git commit -m "feat(aero): balanced-magnitude model anchored at 47% front weight"
```

---

## Task 3: Rework the handling-bias aero lever (balance-space, kit-independent)

**Files:**
- Modify: `Fh6Tuning.Tests/AeroModelTests.cs` (add dial tests)
- Modify: `Fh6Tuning.Core/TuningEngine.cs` (`ApplyHandlingBias` aero block)

- [ ] **Step 1: Add the failing dial tests**

Append these to `AeroModelTests.cs` (inside the class):

```csharp
    [Fact] // The dial's aero shift is the SAME in balance terms regardless of kit ranges.
    public void HandlingBiasAero_IsKitIndependent()
    {
        var std = Car(Drivetrain.AWD, EngineLocation.Front, 50, 450, StdF, StdR);
        var big = Car(Drivetrain.AWD, EngineLocation.Front, 50, 450, BigF, BigR);
        double dStd = Share(Engine.Compute(std with { HandlingBias = 5 }, Goal.Circuit)) - Share(Engine.Compute(std, Goal.Circuit));
        double dBig = Share(Engine.Compute(big with { HandlingBias = 5 }, Goal.Circuit)) - Share(Engine.Compute(big, Goal.Circuit));
        Assert.True(Math.Abs(dStd - dBig) < 0.02, $"dial aero shift kit-dependent: STD Δ{dStd:0.000} vs BIG Δ{dBig:0.000}");
        Assert.True(dStd > 0.03, $"+5 bias barely moved balance: Δ{dStd:0.000}");
    }

    [Fact] // +bias raises front-share (toward oversteer); -bias lowers it.
    public void HandlingBiasAero_ShiftsBalanceDirectionally()
    {
        var car = Car(Drivetrain.AWD, EngineLocation.Front, 50, 450, BigF, BigR);
        double bn = Share(Engine.Compute(car, Goal.Circuit));
        double bp = Share(Engine.Compute(car with { HandlingBias = 5 }, Goal.Circuit));
        double bm = Share(Engine.Compute(car with { HandlingBias = -5 }, Goal.Circuit));
        Assert.True(bp > bn && bn > bm, $"not monotone: -5 {bm:0.00}, 0 {bn:0.00}, +5 {bp:0.00}");
    }
```

- [ ] **Step 2: Run the dial tests to verify they FAIL**

Run: `dotnet test Fh6Tuning.sln --filter "FullyQualifiedName~HandlingBiasAero"`
Expected: FAIL — `HandlingBiasAero_IsKitIndependent` fails because the current ±8%-of-slider lever moves STD and BIG by different balance amounts.

- [ ] **Step 3: Replace the `ApplyHandlingBias` aero block**

In `Fh6Tuning.Core/TuningEngine.cs`, replace the block that starts at the comment `/* ---- Aero front/rear ratio (±8 pts each end, exp 1.05) ---- */` (currently line 1006) and ends at the closing `}` before `return t with { ... }` (currently line 1033) with:

```csharp
        /* ---- Aero front/rear BALANCE (±0.08 front-share at ±5, exp 1.05) ---- */
        if (aero.Applicable && t.Goal != Goal.Drag)
        {
            double s = BiasScale(bias, 1.05); // + → more front DF / less rear DF (toward oversteer)
            AeroRange fR = input.AeroFront, rR = input.AeroRear;
            double? BiasLbf(double pct, AeroRange rng) => rng.HasRange
                ? JsMath.Round(rng.Min!.Value + (rng.Max!.Value - rng.Min!.Value) * Clamp(pct / 100, 0, 1))
                : (double?)null;

            if (aero.Front != null && aero.Rear != null)
            {
                // Full kit: shift the aero BALANCE (front-share), preserving total downforce. Work in
                // lbf when ranges are known (kit-independent), else in %-share. This mirrors the
                // baseline's magnitude model so the dial feels the same on every car's kit.
                bool useLbf = aero.FrontLbf is double && aero.RearLbf is double && fR.HasRange && rR.HasRange;
                double fVal = useLbf ? aero.FrontLbf!.Value : aero.Front.Value;
                double rVal = useLbf ? aero.RearLbf!.Value : aero.Rear.Value;
                double total = fVal + rVal;
                if (total > 0)
                {
                    double newShare = Clamp(fVal / total + 0.08 * s, 0, 1);
                    double nf = total * newShare, nr = total * (1 - newShare);
                    if (useLbf)
                    {
                        nf = Clamp(nf, fR.Min!.Value, fR.Max!.Value);
                        nr = Clamp(nr, rR.Min!.Value, rR.Max!.Value);
                        aero = aero with
                        {
                            Front = R5((nf - fR.Min.Value) / (fR.Max.Value - fR.Min.Value) * 100),
                            FrontLbf = JsMath.Round(nf),
                            Rear = R5((nr - rR.Min.Value) / (rR.Max.Value - rR.Min.Value) * 100),
                            RearLbf = JsMath.Round(nr),
                        };
                    }
                    else
                    {
                        aero = aero with { Front = R5(Clamp(nf, 0, 100)), Rear = R5(Clamp(nr, 0, 100)) };
                    }
                    aero = aero with
                    {
                        Why = BiasNote(aero.Why, bias > 0
                            ? "aero balance shifted forward → more front high-speed grip, looser rear → toward oversteer"
                            : "aero balance shifted rearward → rear planted at speed, lighter nose → toward understeer"),
                    };
                }
            }
            else if (aero.Front != null)
            {
                // Single front splitter — can't rebalance; nudge the present end ±8% of its range.
                double nf = R5(Clamp(aero.Front.Value + 8 * s, 0, 100));
                aero = aero with { Front = nf, FrontLbf = BiasLbf(nf, fR),
                    Why = BiasNote(aero.Why, bias > 0 ? "front downforce raised → toward oversteer" : "front downforce lowered → toward understeer") };
            }
            else if (aero.Rear != null)
            {
                // Single rear wing — nudge the present end ∓8% of its range.
                double nr = R5(Clamp(aero.Rear.Value - 8 * s, 0, 100));
                aero = aero with { Rear = nr, RearLbf = BiasLbf(nr, rR),
                    Why = BiasNote(aero.Why, bias > 0 ? "rear downforce lowered → toward oversteer" : "rear downforce raised → toward understeer") };
            }
        }
```

- [ ] **Step 4: Run the dial tests — expect PASS**

Run: `dotnet test Fh6Tuning.sln --filter "FullyQualifiedName~HandlingBiasAero"`
Expected: PASS (both tests).

- [ ] **Step 5: Run the full suite — expect GREEN**

Run: `dotnet test Fh6Tuning.sln`
Expected: PASS. `SweepTests.ConfigSweep_RangeNeutralityAndDescent` confirms bias-0 neutrality (aero unchanged at bias 0 — the post-process is skipped) and that aero % stays in [0,100] across the full bias sweep.

- [ ] **Step 6: Commit**

```bash
git add Fh6Tuning.Core/TuningEngine.cs Fh6Tuning.Tests/AeroModelTests.cs
git commit -m "feat(aero): handling-bias shifts aero balance in lbf space, kit-independent"
```

---

## Task 4: Update specs and docs

**Files:**
- Modify: `research/spec-aero-diff.md`
- Modify: `research/spec-handling-bias.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Correct the AERO section of `research/spec-aero-diff.md`**

Replace the "CATEGORY 1: AERO" core model (§1.1) drivetrain-balance + AWD-override description with the balanced-magnitude model. Add a subsection documenting the deep-research findings:

```markdown
### 1.1 Core model (REVISED 2026-06-14 — balanced-magnitude)

Aero balance is set by **weight distribution**, not drivetrain. Anchored at a **47% front-weight
ideal** (QuickTune-canonical, verified): at 47% front the target is **balanced downforce** (front ≈
rear in lbf). Front sits at the goal LEVEL of its slider range; rear targets the same magnitude,
trimmed **+1.867 lbf per 1%** of front-weight above 47% (front-heavy → more rear). Solve in **lbf
magnitude**, never the in-game Aero Balance stat (its front-share vs rear-share direction is
unverified — see findings). Clamp each end to its part range.

- **No drivetrain slider rule.** The old "AWD/FWD = max front / min rear" was a slider-position
  artifact calibrated to ONE kit (Standard Forza, where front-max ≈ rear-min); on a big-rear-wing kit
  it produces a ~0.75 front-share. Drivetrain-specific balance ratios (e.g. "AWD 0.40-0.45") were
  REFUTED in 3-vote adversarial verification — do NOT reintroduce them.
- **LEVEL** (Circuit 0.85 … Drag 0.0) sets total downforce (grip vs top speed) — orthogonal to balance.
- **Rear-engine safeguard:** never below 0.50 front-share (rear lbf ≥ front lbf).
- **Engine location** affects the target only THROUGH weight distribution (+ the rear-engine
  safeguard) — no independent engine-location rear-shed (the old `-5%/-10%` mid/rear trim was backwards
  for a loose rear and is removed).

Verified anchors (deep-research 2026-06-14): 47% reference + 1.867 lb/% are QuickTune conventions
(community-canonical, not Turn 10-official). The Aero Balance stat DIRECTION could not be settled —
present lbf + slider %, treat any front-share readout as informational.
```

- [ ] **Step 2: Correct the aero lever in `research/spec-handling-bias.md`**

In the §3 per-lever table, replace the **Aero** row, and update the §1 bullet, to describe the balance-space shift:

```markdown
| **Aero** front/rear | ±0.08 front-share | **1.05** | Shifts the aero BALANCE (front's share of total downforce), preserving total, solved in lbf so the effect is identical across kits. +bias raises front-share (toward oversteer); −bias lowers it. Single-wing cars nudge the lone end ±8% of its range. |
```

And the §1 bullet:

```markdown
- **Aero:** shift the front/rear downforce balance — raise front-share for a looser rear at speed
  (oversteer), lower it to plant the rear (understeer). (spec-aero-diff §1.1)
```

- [ ] **Step 3: Note the aero carve-out in `CLAUDE.md`**

In the parity-harness section of `CLAUDE.md`, add a sentence:

```markdown
**Aero is exempt from the parity gate (as of 2026-06-14).** The aero category was migrated to a
balanced-magnitude model validated by C#-native tests (`Fh6Tuning.Tests/AeroModelTests.cs`); its
numeric + why leaves are skipped in `ParityHarness` (`IsAeroExempt`). This is the first carve-out of
the planned parity-layer deprecation — `legacy/tuning.js` aero is intentionally stale.
```

- [ ] **Step 4: Verify docs build nothing / run the suite**

Run: `dotnet test Fh6Tuning.sln`
Expected: PASS (docs are not compiled; this confirms nothing regressed).

- [ ] **Step 5: Commit**

```bash
git add research/spec-aero-diff.md research/spec-handling-bias.md CLAUDE.md
git commit -m "docs(aero): document balanced-magnitude model + parity carve-out"
```

---

## Task 5: Cleanup and final verification

**Files:**
- Delete: `aero-verify.cjs`

- [ ] **Step 1: Delete the throwaway verification script**

```bash
git rm --cached aero-verify.cjs 2>$null; Remove-Item -Force aero-verify.cjs
```
(If it was never staged, just `Remove-Item -Force aero-verify.cjs`.)

- [ ] **Step 2: Full clean test run**

Run: `dotnet test Fh6Tuning.sln`
Expected: PASS — all of `AeroModelTests`, `ParityTests` (aero-exempt), `SweepTests`, and the rest.

- [ ] **Step 3: Spot-check the user's real car in the Web app (optional but recommended)**

Launch the dev server (`.claude/launch.json` `web`, port 5221), enter the Cayman (AWD/Mid/47%/630hp, front 235–704, rear 240–1038, Circuit) and confirm the aero card now reads ≈ front 90% / rear 50% (≈650 lbf each, balance ≈ 0.50) instead of 100/10.

- [ ] **Step 4: Commit cleanup**

```bash
git add -A
git commit -m "chore: remove throwaway aero verification script"
```

---

## Self-Review

**Spec coverage:**
- Balanced-magnitude baseline (weight-anchored, 1.867 lb/%, LEVEL preserved) → Task 2.
- Pure weight model, RWD/FWD included (no drivetrain ratio) → Task 2 (no drivetrain term in the new block).
- Rear-engine safeguard (never below 0.50 share) → Task 2 + `RearEngine_NeverBelowBalanced`.
- Drop AWD override + mid/rear engine rear-shed; aero stops reading over/understeer flags → Task 2 (block replaced).
- Handling-bias aero in balance/lbf space, kit-independent → Task 3 + `HandlingBiasAero_IsKitIndependent`.
- Option B: aero exempt from parity, no JS change → Task 1.
- Docs/specs updated → Task 4.

**Placeholder scan:** none — every step has exact code/commands.

**Type consistency:** `Aero` record fields (`Front`, `FrontLbf`, `Rear`, `RearLbf`, `Why`, `Applicable`) match Tune.cs; helpers (`R5`, `Clamp`, `JsMath.Round`, `BiasScale`, `BiasNote`, `GoalName`, `S`, `ToLbf`, `HasRange`, `lbfNote`, `lbfFormula`, `level`) are all in scope in the targeted methods; `AeroRange.HasRange`/`Min`/`Max` per AeroRange.cs; `Share`/`Car` helpers defined in AeroModelTests before use.

**Note on dial sign:** `+bias` = toward oversteer (raises front-share). Confirmed consistent between baseline why-text and Task 3 dial code and the spec.
