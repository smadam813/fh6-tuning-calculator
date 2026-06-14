using System.Text.Json;
using System.Text.Json.Nodes;
using Fh6Tuning.Core;
using Fh6Tuning.Core.Serialization;

namespace Fh6Tuning.Tests;

/// <summary>
/// Shared plumbing for the differential parity harness: locating + loading
/// <c>parity/cases.json</c>, deserializing a JS-produced input object into the Core
/// <see cref="TuneInput"/> record, serializing a computed <see cref="Tune"/> back into a JSON tree
/// with the SAME camelCase / enum-token shape the JS engine emits, and walking two JSON trees to
/// collect numeric-leaf and why-string mismatches.
/// </summary>
public static class ParityHarness
{
    /// <summary>One row of <c>parity/cases.json</c>.</summary>
    public sealed record ParityCase(string Id, JsonObject Input, string Goal, JsonObject Expected);

    /// <summary>A single numeric-leaf disagreement between JS and C#.</summary>
    public sealed record NumericMismatch(string Id, string Path, double Expected, double Actual);

    /// <summary>A single why-string (text/formula) disagreement between JS and C#.</summary>
    public sealed record StringMismatch(string Id, string Path, string Expected, string Actual);

    /// <summary>A single boolean-leaf disagreement between JS and C# (hard gate).</summary>
    public sealed record BoolMismatch(string Id, string Path, bool Expected, bool Actual);

    /// <summary>
    /// A shape mismatch where C# emitted a real (non-null) value at a path the JS engine left
    /// <c>null</c> or omitted — caught only by the bidirectional (actual-side) walk. <see cref="Kind"/>
    /// names the offending C# leaf type (e.g. <c>number</c>, <c>true</c>).
    /// </summary>
    public sealed record ExtraLeaf(string Id, string Path, string Kind);

    /// <summary>The full set of leaf disagreements collected for one parity case.</summary>
    public sealed class CompareResult
    {
        public List<NumericMismatch> Numeric { get; } = [];
        public List<StringMismatch> Strings { get; } = [];
        public List<BoolMismatch> Bools { get; } = [];
        public List<ExtraLeaf> Extra { get; } = [];

        /// <summary>Hard-gate failures: numeric + boolean + summary-string + extra-leaf disagreements.
        /// (why.text/why.formula remain a SEPARATE soft category and are NOT counted here.)</summary>
        public int HardCount => Numeric.Count + Bools.Count + Extra.Count + SummaryStringCount;

        /// <summary>Count of hard summary-chip string mismatches (excludes the soft why.* strings).</summary>
        public int SummaryStringCount => Strings.Count(s => IsSummaryString(s.Path));

        /// <summary>The soft why.text/why.formula string mismatches.</summary>
        public IEnumerable<StringMismatch> WhyStrings => Strings.Where(s => IsWhyString(s.Path));
    }

    // Round-trip options that mirror legacy/tuning.js leaf names and enum tokens:
    //  • camelCase property names (Front -> "front", FrontWeightPct -> "frontWeightPct")
    //  • enums via their per-type JsonStringEnumConverter ("Circuit", "RWD", ...)
    //  • nulls emitted (the JS object keeps null fields like speeds/frontLbf)
    // This is the deserialize side AND the serialize-the-result side, so both sit in one place.
    public static readonly JsonSerializerOptions RoundTrip = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    };

    /// <summary>
    /// Walk up from the test assembly until we find the repo's <c>parity/cases.json</c>.
    /// </summary>
    public static string CasesPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "parity", "cases.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate parity/cases.json by walking up from " + AppContext.BaseDirectory +
            ". Run `node legacy/parity-export.js` to generate it.");
    }

    /// <summary>Load and parse every parity case from disk.</summary>
    public static IReadOnlyList<ParityCase> LoadCases()
    {
        string json = File.ReadAllText(CasesPath());
        JsonArray arr = JsonNode.Parse(json)!.AsArray();
        var cases = new List<ParityCase>(arr.Count);
        foreach (JsonNode? node in arr)
        {
            JsonObject obj = node!.AsObject();
            cases.Add(new ParityCase(
                Id: (string)obj["id"]!,
                Input: obj["input"]!.AsObject(),
                Goal: (string)obj["goal"]!,
                Expected: obj["expected"]!.AsObject()));
        }
        return cases;
    }

    /// <summary>Deserialize the JS input object (camelCase) into the Core input record.</summary>
    public static TuneInput ToInput(JsonObject input) =>
        input.Deserialize<TuneInput>(RoundTrip)
        ?? throw new JsonException("input deserialized to null");

    /// <summary>Map the JS goal token to the Core enum.</summary>
    public static Goal ToGoal(string goal) => Enum.Parse<Goal>(goal);

    /// <summary>Serialize a computed tune back into a JSON tree in JS leaf-name / enum-token shape.</summary>
    public static JsonObject ToTree(Tune tune) =>
        JsonSerializer.SerializeToNode(tune, RoundTrip)!.AsObject();

    /// <summary>
    /// Compare a JS <paramref name="expected"/> tree against the C# <paramref name="actual"/> tree,
    /// in BOTH directions:
    /// <list type="bullet">
    ///   <item><b>Forward</b> (over <paramref name="expected"/>): every numeric leaf is a hard
    ///   bit-for-bit gate (after -0 normalization); every <b>boolean</b> leaf (singleSpeed, applicable,
    ///   isEV, oversteerProne, …) is a hard gate; <c>summary[].k</c>/<c>summary[].v</c> chip strings are
    ///   a hard string gate; <c>why.text</c>/<c>why.formula</c> stay a SEPARATE soft category. A JS
    ///   leaf the C# side fails to reproduce (number/bool/string vs null-or-missing) is a mismatch.</item>
    ///   <item><b>Backward</b> (over <paramref name="actual"/>): any C# leaf that is a real value
    ///   (non-null number, <c>true</c>, or a populated array element) where the JS side is <c>null</c>
    ///   or absent is reported as an <see cref="ExtraLeaf"/> — this catches a regressed single-wing
    ///   suppression (number where JS emitted null) or a spuriously-emitted speeds/topSpeed.</item>
    /// </list>
    /// </summary>
    public static CompareResult Compare(string id, JsonNode? expected, JsonNode? actual)
    {
        var r = new CompareResult();
        Walk(id, "", expected, actual, r);
        WalkExtra(id, "", expected, actual, r);
        return r;
    }

    private static void Walk(string id, string path, JsonNode? expected, JsonNode? actual, CompareResult r)
    {
        switch (expected)
        {
            case JsonObject expObj:
            {
                JsonObject? actObj = actual as JsonObject;
                foreach (var kvp in expObj)
                {
                    string childPath = path.Length == 0 ? kvp.Key : path + "." + kvp.Key;
                    JsonNode? actChild = actObj is not null && actObj.TryGetPropertyValue(kvp.Key, out JsonNode? c) ? c : null;
                    Walk(id, childPath, kvp.Value, actChild, r);
                }
                break;
            }
            case JsonArray expArr:
            {
                JsonArray? actArr = actual as JsonArray;
                for (int i = 0; i < expArr.Count; i++)
                {
                    string childPath = $"{path}[{i}]";
                    JsonNode? actChild = actArr is not null && i < actArr.Count ? actArr[i] : null;
                    Walk(id, childPath, expArr[i], actChild, r);
                }
                break;
            }
            case JsonValue expVal:
            {
                if (TryGetNumber(expVal, out double expNum))
                {
                    // numeric leaf — the hard parity gate
                    double expN = Norm(expNum);
                    if (actual is JsonValue actVal && TryGetNumber(actVal, out double actNum))
                    {
                        double actN = Norm(actNum);
                        if (!Equal(expN, actN))
                            r.Numeric.Add(new NumericMismatch(id, path, expN, actN));
                    }
                    else
                    {
                        // expected a number, C# produced something else (or missing) — count as mismatch
                        double actN = actual is JsonValue av && TryGetNumber(av, out double an) ? Norm(an) : double.NaN;
                        r.Numeric.Add(new NumericMismatch(id, path, expN, actN));
                    }
                }
                else if (TryGetBool(expVal, out bool expBool))
                {
                    // boolean leaf — hard gate (singleSpeed / applicable / isEV / understeerProne / …)
                    bool actBool = actual is JsonValue ab && TryGetBool(ab, out bool b) && b;
                    if (expBool != actBool)
                        r.Bools.Add(new BoolMismatch(id, path, expBool, actBool));
                }
                else if (expVal.TryGetValue(out string? expStr) && expStr is not null)
                {
                    // string leaf: why.* (soft) AND summary chip k/v (hard) are gated; enum tokens
                    // (drivetrain echoes, classTier, goal) are deterministic but compared via the
                    // numeric/bool/structure around them, so they stay out of the string gates.
                    if (IsWhyString(path) || IsSummaryString(path))
                    {
                        string actStr = (actual as JsonValue)?.TryGetValue(out string? s) == true ? s ?? "" : "";
                        if (!string.Equals(expStr, actStr, StringComparison.Ordinal))
                            r.Strings.Add(new StringMismatch(id, path, expStr, actStr));
                    }
                }
                // expected null leaf (speeds/frontLbf/center = null) → the backward walk verifies
                // the C# side is also null/absent there.
                break;
            }
            // expected null node → handled by the backward walk.
        }
    }

    // Backward walk: report C# leaves that carry a real value where JS is null or absent.
    private static void WalkExtra(string id, string path, JsonNode? expected, JsonNode? actual, CompareResult r)
    {
        switch (actual)
        {
            case JsonObject actObj:
            {
                JsonObject? expObj = expected as JsonObject;
                foreach (var kvp in actObj)
                {
                    string childPath = path.Length == 0 ? kvp.Key : path + "." + kvp.Key;
                    JsonNode? expChild = expObj is not null && expObj.TryGetPropertyValue(kvp.Key, out JsonNode? c) ? c : null;
                    WalkExtra(id, childPath, expChild, kvp.Value, r);
                }
                break;
            }
            case JsonArray actArr:
            {
                JsonArray? expArr = expected as JsonArray;
                for (int i = 0; i < actArr.Count; i++)
                {
                    string childPath = $"{path}[{i}]";
                    JsonNode? expChild = expArr is not null && i < expArr.Count ? expArr[i] : null;
                    WalkExtra(id, childPath, expChild, actArr[i], r);
                }
                break;
            }
            case JsonValue actVal:
            {
                // Only flag when the JS side is null/absent (a value-vs-value disagreement is already
                // caught by the forward walk; we don't want to double-count it here).
                bool jsIsNull = expected is null || expected.GetValueKind() == System.Text.Json.JsonValueKind.Null;
                if (!jsIsNull) break;

                if (TryGetNumber(actVal, out double n))
                    r.Extra.Add(new ExtraLeaf(id, path, $"number({Fmt(Norm(n))})"));
                else if (TryGetBool(actVal, out bool b) && b)
                    r.Extra.Add(new ExtraLeaf(id, path, "true"));
                // C# null / false / empty-string where JS is null is benign (matches JS's missing leaf).
                break;
            }
        }
    }

    private static bool IsWhyString(string path) =>
        path.EndsWith(".why.text", StringComparison.Ordinal) ||
        path.EndsWith(".why.formula", StringComparison.Ordinal);

    // summary is an array of { k, v } chips; both are deterministic formatted engine outputs.
    private static bool IsSummaryString(string path) =>
        path.StartsWith("summary[", StringComparison.Ordinal) &&
        (path.EndsWith(".k", StringComparison.Ordinal) || path.EndsWith(".v", StringComparison.Ordinal));

    // A JsonValue is numeric only if it round-trips to a double AND is not a string/bool.
    private static bool TryGetNumber(JsonValue value, out double number)
    {
        if (value.GetValueKind() == System.Text.Json.JsonValueKind.Number && value.TryGetValue(out double d))
        {
            number = d;
            return true;
        }
        number = 0;
        return false;
    }

    // A JsonValue is boolean only if its kind is True/False.
    private static bool TryGetBool(JsonValue value, out bool b)
    {
        var k = value.GetValueKind();
        if (k == System.Text.Json.JsonValueKind.True) { b = true; return true; }
        if (k == System.Text.Json.JsonValueKind.False) { b = false; return true; }
        b = false;
        return false;
    }

    /// <summary>Shortest invariant rendering of a numeric leaf for diagnostics.</summary>
    public static string Fmt(double x) =>
        double.IsNaN(x) ? "<missing/non-number>" : x.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // JSON.stringify(-0) === "0"; normalize so a JS 0 and a C# -0 (or vice versa) agree.
    private static double Norm(double x) => x == 0.0 ? 0.0 : x;

    // Byte-for-byte numeric equality. NaN==NaN treated as equal so a "both missing/NaN" case
    // is not double-counted, but a real value vs NaN IS a mismatch (handled by the caller path).
    private static bool Equal(double a, double b)
    {
        if (double.IsNaN(a) && double.IsNaN(b)) return false; // expected-number vs non-number actual
        return a.Equals(b);
    }
}
