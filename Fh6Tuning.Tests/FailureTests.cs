using System.Text.RegularExpressions;
using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// FAILURE TESTS — <c>validate()</c> is the input contract the UI relies on to refuse rendering a
/// broken tune (the C# port of <c>legacy/test/failure.test.js</c>). Asserts it FLAGS nonsense
/// (missing required numerics, non-finite values, weight≤0, front weight ≤0 or ≥100, negative power,
/// gears&lt;1, inverted/non-positive ranges) and ACCEPTS reasonable input; also that
/// <c>compute()</c> stays defensive (never throws, always clamps) so a bad value can't crash it.
///
/// <para>validate() takes a <see cref="RawInput"/> of <see cref="RawValue"/>s so absent / blank /
/// non-numeric / NaN / Infinity are all representable exactly as JS sees them.</para>
/// </summary>
public sealed class FailureTests
{
    private static readonly ITuningEngine Engine = new TuningEngine();

    // The bare minimum required numerics (legacy goodMin) as a RawInput of numbers.
    private static RawInput GoodMin() => new()
    {
        Power = RawValue.Num(400),
        Weight = RawValue.Num(3300),
        FrontWeightPct = RawValue.Num(52),
    };

    private static void ExpectInvalid(RawInput input, string? pattern, string label)
    {
        var res = Engine.Validate(input);
        Assert.False(res.Valid, $"{label}: expected invalid");
        Assert.True(res.Errors.Count > 0, $"{label}: expected errors array");
        if (pattern is not null)
            Assert.True(res.Errors.Any(e => Regex.IsMatch(e, pattern, RegexOptions.IgnoreCase)),
                $"{label}: no error matched {pattern}, got {string.Join(" | ", res.Errors)}");
    }

    /* ---------------- valid baseline ---------------- */
    [Fact]
    public void Validate_AcceptsCompleteSaneInput()
    {
        // baseInput() — fill the required raw numerics (validate only reads those + optional ranges).
        var res = Engine.Validate(new RawInput
        {
            Power = RawValue.Num(400),
            Torque = RawValue.Num(370),
            Weight = RawValue.Num(3300),
            FrontWeightPct = RawValue.Num(52),
            Gears = RawValue.Num(6),
            SpringRateMinF = RawValue.Num(150),
            SpringRateMaxF = RawValue.Num(900),
            SpringRateMinR = RawValue.Num(150),
            SpringRateMaxR = RawValue.Num(900),
            RideHeightMinF = RawValue.Num(4.5),
            RideHeightMaxF = RawValue.Num(7.0),
            RideHeightMinR = RawValue.Num(4.5),
            RideHeightMaxR = RawValue.Num(7.0),
        });
        Assert.True(res.Valid);
        Assert.Empty(res.Errors);
    }

    [Fact]
    public void Validate_AcceptsBareMinimumRequiredNumerics()
    {
        var res = Engine.Validate(GoodMin());
        Assert.True(res.Valid, string.Join(" | ", res.Errors));
    }

    /* ---------------- missing required inputs ---------------- */
    [Fact]
    public void MissingWeight_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), FrontWeightPct = RawValue.Num(52) }, "weight", "missing weight");

    [Fact]
    public void MissingPower_Flagged() =>
        ExpectInvalid(new RawInput { Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(52) }, "power", "missing power");

    [Fact]
    public void MissingFrontWeightPct_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(3300) }, "front weight", "missing frontWeightPct");

    [Fact]
    public void EmptyObject_ReportsMultipleErrors()
    {
        var res = Engine.Validate(new RawInput());
        Assert.False(res.Valid);
        Assert.True(res.Errors.Count >= 3, "empty input should flag all three required fields");
    }

    /* ---------------- non-finite required numerics ---------------- */
    [Fact]
    public void NaNWeight_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(double.NaN), FrontWeightPct = RawValue.Num(52) }, "weight", "NaN weight");

    [Fact]
    public void InfinityPower_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(double.PositiveInfinity), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(52) }, "power", "Infinity power");

    [Fact]
    public void NonNumericStringWeight_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Str("heavy"), FrontWeightPct = RawValue.Num(52) }, "weight", "string weight");

    /* ---------------- out-of-range physical nonsense ---------------- */
    [Fact]
    public void NegativeWeight_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(-100), FrontWeightPct = RawValue.Num(52) }, "weight.*greater than 0", "negative weight");

    [Fact]
    public void ZeroWeight_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(0), FrontWeightPct = RawValue.Num(52) }, "weight.*greater than 0", "zero weight");

    [Fact]
    public void FrontWeightPctAtOrAbove100_Flagged()
    {
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(120) }, "front weight.*less than 100", ">100 front");
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(100) }, "front weight.*less than 100", "=100 front");
    }

    [Fact]
    public void FrontWeightPctAtOrBelow0_Flagged()
    {
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(0) }, "front weight.*greater than 0", "0 front");
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(-5) }, "front weight.*greater than 0", "negative front");
    }

    [Fact]
    public void NegativePower_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(-10), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(52) }, "power.*negative", "negative power");

    [Fact]
    public void PowerExactly0_Allowed()
    {
        var res = Engine.Validate(new RawInput { Power = RawValue.Num(0), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(52) });
        Assert.True(res.Valid, string.Join(" | ", res.Errors));
    }

    [Fact]
    public void GearsLessThan1_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(52), Gears = RawValue.Num(0) }, "gears.*at least 1", "0 gears");

    [Fact]
    public void NegativeTorque_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(52), Torque = RawValue.Num(-50) }, "torque.*negative", "negative torque");

    [Fact]
    public void InvertedSpringRange_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(52), SpringRateMin = RawValue.Num(900), SpringRateMax = RawValue.Num(150) }, "spring rate max", "inverted springs");

    [Fact]
    public void NonPositiveSpringMin_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(52), SpringRateMin = RawValue.Num(0), SpringRateMax = RawValue.Num(900) }, "spring rate min", "zero spring min");

    [Fact]
    public void InvertedRideHeightRange_Flagged() =>
        ExpectInvalid(new RawInput { Power = RawValue.Num(400), Weight = RawValue.Num(3300), FrontWeightPct = RawValue.Num(52), RideHeightMin = RawValue.Num(7), RideHeightMax = RawValue.Num(4) }, "ride height max", "inverted ride");

    /* ---------------- multiple problems reported together ---------------- */
    [Fact]
    public void MultipleInvalidFields_AllReported()
    {
        var res = Engine.Validate(new RawInput { Power = RawValue.Num(-5), Weight = RawValue.Num(-1), FrontWeightPct = RawValue.Num(150) });
        Assert.False(res.Valid);
        Assert.True(res.Errors.Count >= 3, $"expected >=3 errors, got {res.Errors.Count}");
        Assert.Contains(res.Errors, e => Regex.IsMatch(e, "power", RegexOptions.IgnoreCase));
        Assert.Contains(res.Errors, e => Regex.IsMatch(e, "weight.*greater than 0", RegexOptions.IgnoreCase));
        Assert.Contains(res.Errors, e => Regex.IsMatch(e, "front weight", RegexOptions.IgnoreCase));
    }

    /* ---------------- compute() stays defensive ---------------- */
    [Fact]
    public void Compute_DoesNotThrowOnOutOfRange_StillClamps()
    {
        var nonsense = Fixtures.BaseInput(b => { b.FrontWeightPct = 150; b.Weight = 99999; b.Power = 5000; });
        Tune t = Fixtures.TUNING.Compute(nonsense, Goal.Circuit);
        Helpers.AssertAllInRange(t);
    }

    [Fact]
    public void Compute_ClampsExtremeLowWeightLowPowerCar()
    {
        var tiny = Fixtures.BaseInput(b => { b.Weight = 200; b.Power = 1; b.FrontWeightPct = 10; b.PiClass = PiClass.D; });
        Tune t = Fixtures.TUNING.Compute(tiny, Goal.OffRoad);
        Helpers.AssertAllInRange(t);
    }

    [Fact]
    public void Compute_HandlesAllGoalsWithoutThrowing()
    {
        var car = Fixtures.BaseInput();
        foreach (Goal g in Fixtures.TUNING.Goals)
        {
            var ex = Record.Exception(() => Fixtures.TUNING.Compute(car, g));
            Assert.Null(ex);
        }
    }
}
