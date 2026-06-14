using System.Text;
using System.Text.Json.Nodes;
using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// Differential parity against the legacy JS engine. Loads <c>parity/cases.json</c> (produced by
/// <c>legacy/parity-export.js</c>), replays each input through the Core engine, and asserts the JS
/// <c>expected</c> snapshot is reproduced EXACTLY. The hard gate spans, bidirectionally:
/// <list type="bullet">
///   <item>every numeric leaf (bit-for-bit after -0 normalization);</item>
///   <item>every boolean leaf — singleSpeed / applicable / isEV / under-&amp;over-steerProne /
///   canTuneSusp (these define tune SHAPE);</item>
///   <item>every <c>summary[].k</c>/<c>summary[].v</c> chip string (computed corner-load / p:w /
///   balance / class-tier formatting);</item>
///   <item>extra C# leaves: a real value where JS emitted <c>null</c>/absent (e.g. a regressed
///   single-wing suppression or a spurious speeds/topSpeed).</item>
/// </list>
/// Only <c>why.text</c>/<c>why.formula</c> wording stays a SEPARATE soft category so prose drift can
/// never mask a math/shape regression.
/// </summary>
public sealed class ParityTests
{
    // Loaded once; the cases are immutable.
    private static readonly IReadOnlyList<ParityHarness.ParityCase> Cases = ParityHarness.LoadCases();
    private static readonly ITuningEngine Engine = new TuningEngine();

    public static TheoryData<string> CaseIds()
    {
        var data = new TheoryData<string>();
        foreach (var c in Cases) data.Add(c.Id);
        return data;
    }

    /// <summary>
    /// HARD GATE — one test row per parity case. Every numeric leaf, boolean leaf and summary-chip
    /// string the JS engine produced must be reproduced by the Core engine, and the C# tree must not
    /// carry a real value where JS emitted null/absent. On mismatch the message lists id + json-path +
    /// expected + actual for each disagreeing leaf.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseIds))]
    public void NumericParity(string id)
    {
        ParityHarness.ParityCase c = Cases.First(x => x.Id == id);
        Tune tune = Engine.Compute(ParityHarness.ToInput(c.Input), ParityHarness.ToGoal(c.Goal));
        JsonObject actual = ParityHarness.ToTree(tune);

        var r = ParityHarness.Compare(id, c.Expected, actual);

        if (r.HardCount > 0)
            Assert.Fail(DescribeHard($"[{id}]", r));
    }

    /// <summary>
    /// Aggregate hard-parity diagnostic. Runs the whole grid and, if any case disagrees, fails with
    /// the TOTAL mismatch count (numeric + boolean + summary-string + extra-leaf) plus a sample, so a
    /// single run summarizes the remaining JS↔C# gap without scrolling 2000+ individual rows.
    /// </summary>
    [Fact]
    public void HardParity_Aggregate()
    {
        var numeric = new List<ParityHarness.NumericMismatch>();
        var bools = new List<ParityHarness.BoolMismatch>();
        var summary = new List<ParityHarness.StringMismatch>();
        var extra = new List<ParityHarness.ExtraLeaf>();
        int casesWithMismatch = 0;
        foreach (var c in Cases)
        {
            Tune tune = Engine.Compute(ParityHarness.ToInput(c.Input), ParityHarness.ToGoal(c.Goal));
            JsonObject actual = ParityHarness.ToTree(tune);
            var r = ParityHarness.Compare(c.Id, c.Expected, actual);
            if (r.HardCount > 0) casesWithMismatch++;
            numeric.AddRange(r.Numeric);
            bools.AddRange(r.Bools);
            summary.AddRange(r.Strings.Where(s => IsSummary(s.Path)));
            extra.AddRange(r.Extra);
        }

        int total = numeric.Count + bools.Count + summary.Count + extra.Count;
        if (total > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"HARD PARITY: {total} leaf mismatch(es) across {casesWithMismatch}/{Cases.Count} cases " +
                          $"(numeric {numeric.Count}, bool {bools.Count}, summary {summary.Count}, extra {extra.Count}).");
            foreach (var m in numeric.Take(20))
                sb.AppendLine($"  [num] [{m.Id}] {m.Path}: expected {ParityHarness.Fmt(m.Expected)} but got {ParityHarness.Fmt(m.Actual)}");
            foreach (var m in bools.Take(20))
                sb.AppendLine($"  [bool] [{m.Id}] {m.Path}: expected {m.Expected} but got {m.Actual}");
            foreach (var m in summary.Take(20))
                sb.AppendLine($"  [sum] [{m.Id}] {m.Path}: expected \"{m.Expected}\" but got \"{m.Actual}\"");
            foreach (var m in extra.Take(20))
                sb.AppendLine($"  [extra] [{m.Id}] {m.Path}: C# emitted {m.Kind} where JS is null/absent");
            Assert.Fail(sb.ToString());
        }
    }

    /// <summary>
    /// SOFT CATEGORY — why.text / why.formula string parity. Reported separately so it never gates
    /// the hard parity. Fails with the count + a sample when wording drifts, but a green NumericParity
    /// is the real contract.
    /// </summary>
    [Fact]
    public void WhyStringParity_Soft()
    {
        var all = new List<ParityHarness.StringMismatch>();
        int casesWithMismatch = 0;
        foreach (var c in Cases)
        {
            Tune tune = Engine.Compute(ParityHarness.ToInput(c.Input), ParityHarness.ToGoal(c.Goal));
            JsonObject actual = ParityHarness.ToTree(tune);
            var r = ParityHarness.Compare(c.Id, c.Expected, actual);
            var why = r.WhyStrings.ToList();
            if (why.Count > 0) casesWithMismatch++;
            all.AddRange(why);
        }

        if (all.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"WHY-STRING PARITY (soft): {all.Count} string mismatch(es) across {casesWithMismatch}/{Cases.Count} cases.");
            sb.AppendLine("First 15:");
            foreach (var m in all.Take(15))
            {
                sb.AppendLine($"  [{m.Id}] {m.Path}");
                sb.AppendLine($"    expected: {Truncate(m.Expected)}");
                sb.AppendLine($"    actual:   {Truncate(m.Actual)}");
            }
            Assert.Fail(sb.ToString());
        }
    }

    private static bool IsSummary(string path) =>
        path.StartsWith("summary[", StringComparison.Ordinal) &&
        (path.EndsWith(".k", StringComparison.Ordinal) || path.EndsWith(".v", StringComparison.Ordinal));

    private static string DescribeHard(string prefix, ParityHarness.CompareResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{prefix} {r.HardCount} hard leaf mismatch(es):");
        foreach (var m in r.Numeric)
            sb.AppendLine($"  [num] {m.Path}: expected {ParityHarness.Fmt(m.Expected)} but got {ParityHarness.Fmt(m.Actual)}");
        foreach (var m in r.Bools)
            sb.AppendLine($"  [bool] {m.Path}: expected {m.Expected} but got {m.Actual}");
        foreach (var m in r.Strings.Where(s => IsSummary(s.Path)))
            sb.AppendLine($"  [sum] {m.Path}: expected \"{m.Expected}\" but got \"{m.Actual}\"");
        foreach (var m in r.Extra)
            sb.AppendLine($"  [extra] {m.Path}: C# emitted {m.Kind} where JS is null/absent");
        return sb.ToString();
    }

    private static string Truncate(string s) => s.Length <= 160 ? s : s[..160] + "…";
}
