using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Fh6Tuning.Core;

/// <summary>
/// One stored Car Setup entry — the C# port of the JS object <c>setups.js</c> validates and
/// stores. Field names mirror the JS keys leaf-for-leaf so serialized JSON round-trips byte-for-byte
/// with the legacy app's localStorage value and backup files.
///
/// <para><b>Why <see cref="JsonObject"/> for <see cref="Fields"/>/<see cref="Dials"/>:</b> in the
/// legacy app these are open bags of arbitrary string-keyed values (form field ids → DOM
/// <c>.value</c> strings; dial ids → strings). Modeling them as <see cref="JsonObject"/> preserves
/// any key set and nesting losslessly, exactly as the JS shallow-copy did, so a backup written by a
/// newer app version survives a round-trip through this one.</para>
///
/// <para><b>Forward compatibility:</b> unknown extra keys land in <see cref="Extra"/> via
/// <see cref="JsonExtensionDataAttribute"/> and are re-emitted on serialize, matching the legacy
/// "kept so a newer backup survives a round-trip" contract.</para>
///
/// This record is the <em>normalized</em>, post-validate shape — produced only by
/// <see cref="SetupsStore.ValidateSetup(JsonNode?)"/>. Construct entries through that, not directly,
/// so the name-trim / safe-default rules are applied.
/// </summary>
public sealed record SavedSetup
{
    /// <summary>Trimmed, non-empty display name. Also the dedupe/identity key throughout the store.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>ISO timestamp string; <c>""</c> when missing (legacy default, never null).</summary>
    [JsonPropertyName("savedAt")]
    public string SavedAt { get; init; } = "";

    /// <summary>"imperial" | "metric"; anything else falls back to "imperial" (legacy default).</summary>
    [JsonPropertyName("units")]
    public string Units { get; init; } = "imperial";

    /// <summary>Goal token string; <c>""</c> when missing or non-string (legacy default).</summary>
    [JsonPropertyName("goal")]
    public string Goal { get; init; } = "";

    /// <summary>Dial id → value bag; <c>{}</c> when missing or not an object (legacy default).</summary>
    [JsonPropertyName("dials")]
    public JsonObject Dials { get; init; } = new();

    /// <summary>Form field id → value bag. Required to be an object for the entry to validate.</summary>
    [JsonPropertyName("fields")]
    public required JsonObject Fields { get; init; }

    /// <summary>
    /// Unknown extra keys preserved verbatim for forward compatibility (legacy kept them via a
    /// shallow copy). Re-emitted (spliced as sibling properties) on serialize after the known keys.
    /// Typed <see cref="Dictionary{TKey,TValue}"/> of <see cref="JsonElement"/> — the extension-data
    /// shape System.Text.Json splices correctly (a <see cref="JsonObject"/> here is mis-serialized as
    /// one nested value). Holds any JSON shape and preserves key order.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = new();
}

/// <summary>
/// The saved-setups database envelope — the C# port of the JS <c>{ schema, setups }</c> object that
/// the UI keeps in localStorage and round-trips through JSON backup files. The store always writes
/// <see cref="SetupsStore.Schema"/> regardless of the schema it read (future-schema tolerance).
/// </summary>
public sealed record SetupsDb
{
    [JsonPropertyName("schema")]
    public required int Schema { get; init; }

    [JsonPropertyName("setups")]
    public required IReadOnlyList<SavedSetup> Setups { get; init; }
}

/// <summary>
/// Outcome of <see cref="SetupsStore.ParseDb(string?)"/>. Mirrors the legacy
/// <c>{ ok, db, skipped }</c> / <c>{ ok, error }</c> union: on success <see cref="Db"/> is set and
/// <see cref="Skipped"/> counts dropped/deduped entries; on failure <see cref="Error"/> explains the
/// unusable envelope and <see cref="Db"/> is <c>null</c>.
/// </summary>
public sealed record ParseDbResult
{
    public required bool Ok { get; init; }
    public SetupsDb? Db { get; init; }
    public int Skipped { get; init; }
    public string? Error { get; init; }

    public static ParseDbResult Success(SetupsDb db, int skipped) =>
        new() { Ok = true, Db = db, Skipped = skipped };

    public static ParseDbResult Failure(string error) =>
        new() { Ok = false, Error = error };
}

/// <summary>
/// Outcome of <see cref="SetupsStore.MergeDb(SetupsDb, SetupsDb)"/>: the merged db plus counts of
/// entries newly <see cref="Added"/> vs. existing ones <see cref="Updated"/> by the import.
/// </summary>
public sealed record MergeDbResult
{
    public required SetupsDb Db { get; init; }
    public required int Added { get; init; }
    public required int Updated { get; init; }
}
