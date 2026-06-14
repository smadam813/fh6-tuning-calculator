using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fh6Tuning.Core;

/// <summary>
/// Pure saved-setups storage logic — the C# port of legacy <c>setups.js</c>. Validates, serializes,
/// parses and merges the saved-setup database the UI keeps in localStorage and round-trips through
/// JSON backup files. <b>No localStorage access of its own</b> — the raw get/set lives in the Web
/// layer's interop service; everything here is a pure, deterministic transform over values, so it is
/// unit-testable exactly like the JS module.
///
/// Every method preserves the documented legacy behavior leaf-for-leaf: name trimming; last-wins
/// dedupe by trimmed name; future-schema tolerance (read entry-by-entry, normalize the envelope to
/// <see cref="Schema"/>); extra-key forward compatibility; imported-wins merge that preserves
/// unrelated existing entries and the imported <c>savedAt</c>.
/// </summary>
public static class SetupsStore
{
    /// <summary>localStorage key the Web layer reads/writes (legacy <c>STORAGE_KEY</c>).</summary>
    public const string StorageKey = "fh6-tuning.setups.v1";

    /// <summary>The schema version this app writes. Reads tolerate any schema &gt;= 1.</summary>
    public const int Schema = 1;

    // Pretty-printed (2-space indent) both in localStorage and in export files — identical format by
    // design, matching JS JSON.stringify(db, null, 2). System.Text.Json's WriteIndented defaults to a
    // 2-space indent, which we pin explicitly so the format can't drift.
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n", // JS JSON.stringify always emits "\n"; pin it so output matches on Windows too.
    };

    // A standalone JSON `null` element, used for extra keys whose value is literally null.
    private static readonly JsonElement NullElement = JsonDocument.Parse("null").RootElement;

    /// <summary>A fresh, empty v1 envelope (legacy <c>emptyDb()</c>).</summary>
    public static SetupsDb EmptyDb() => new() { Schema = Schema, Setups = new List<SavedSetup>() };

    /// <summary>
    /// Normalize a parsed JSON value into a storable <see cref="SavedSetup"/>, or <c>null</c> if it
    /// can't be one (legacy <c>validateSetup</c>). Requires a non-empty (trimmed) string
    /// <c>name</c> and an object <c>fields</c>; <c>units</c>/<c>goal</c>/<c>dials</c>/<c>savedAt</c>
    /// get safe defaults. Unknown extra keys are kept (forward compatibility).
    ///
    /// <para>The JS <c>__proto__</c> neutralization has no C# analog — there is no prototype to
    /// pollute — so a smuggled <c>"__proto__"</c> key is simply preserved as ordinary extra data,
    /// which is harmless here.</para>
    /// </summary>
    public static SavedSetup? ValidateSetup(JsonNode? node)
    {
        // Must be a JSON object (not null, not an array, not a scalar).
        if (node is not JsonObject obj) return null;

        // name: must be a string, non-empty after trim.
        if (obj["name"] is not JsonValue nameVal ||
            !nameVal.TryGetValue(out string? rawName) ||
            rawName is null ||
            rawName.Trim().Length == 0)
        {
            return null;
        }

        // fields: must be an object (not array, not scalar, not missing).
        if (obj["fields"] is not JsonObject fields) return null;

        // units: "metric" stays metric; anything else (incl. missing / non-string) -> "imperial".
        string units = AsStringOrNull(obj["units"]) == "metric" ? "metric" : "imperial";

        // goal: a string passes through; non-string / missing -> "".
        string goal = AsStringOrNull(obj["goal"]) ?? "";

        // savedAt: a string passes through; non-string / missing -> "".
        string savedAt = AsStringOrNull(obj["savedAt"]) ?? "";

        // dials: an object passes through; non-object / missing -> {}.
        JsonObject dials = obj["dials"] as JsonObject ?? new JsonObject();

        // Extra keys: everything that isn't a known key, preserved for forward compatibility.
        var extra = new Dictionary<string, JsonElement>();
        foreach (var kvp in obj)
        {
            switch (kvp.Key)
            {
                case "name":
                case "savedAt":
                case "units":
                case "goal":
                case "dials":
                case "fields":
                    continue;
                default:
                    // Materialize as a detached JsonElement (null node -> JSON null element) so the
                    // value outlives its source tree and round-trips byte-for-byte.
                    extra[kvp.Key] = kvp.Value is null
                        ? NullElement
                        : JsonSerializer.SerializeToElement(kvp.Value);
                    break;
            }
        }

        return new SavedSetup
        {
            Name = rawName.Trim(),
            SavedAt = savedAt,
            Units = units,
            Goal = goal,
            Dials = (JsonObject)dials.DeepClone(),
            Fields = (JsonObject)fields.DeepClone(),
            Extra = extra,
        };
    }

    /// <summary>
    /// Parse a JSON string (localStorage value or backup file) into a db (legacy <c>parseDb</c>).
    /// On success: invalid entries are dropped &amp; counted in <see cref="ParseDbResult.Skipped"/>,
    /// and same-name entries are deduped (last wins, each drop counted). On failure: the envelope
    /// itself is unusable. A schema NEWER than ours is still read entry-by-entry (best effort) and
    /// the resulting db is normalized to <see cref="Schema"/>.
    /// </summary>
    public static ParseDbResult ParseDb(string? jsonString)
    {
        if (jsonString is null) return ParseDbResult.Failure("not valid JSON");

        JsonNode? raw;
        try
        {
            raw = JsonNode.Parse(jsonString);
        }
        catch (JsonException)
        {
            return ParseDbResult.Failure("not valid JSON");
        }

        if (raw is not JsonObject envelope)
            return ParseDbResult.Failure("not a setups backup (expected an object)");

        // schema must be a number >= 1 (a string "1" is rejected, matching JS typeof check).
        if (envelope["schema"] is not JsonValue schemaVal ||
            !TryGetNumber(schemaVal, out double schemaNum) ||
            schemaNum < 1)
        {
            return ParseDbResult.Failure("not a setups backup (bad schema)");
        }

        if (envelope["setups"] is not JsonArray setupsArray)
            return ParseDbResult.Failure("not a setups backup (missing setups list)");

        // Insertion-ordered map dedupes by trimmed name: last entry wins.
        var byName = new Dictionary<string, SavedSetup>();
        var order = new List<string>();
        int skipped = 0;

        foreach (JsonNode? item in setupsArray)
        {
            SavedSetup? entry = ValidateSetup(item);
            if (entry is null) { skipped++; continue; }
            if (byName.ContainsKey(entry.Name)) skipped++;
            else order.Add(entry.Name);
            byName[entry.Name] = entry;
        }

        var setups = new List<SavedSetup>(order.Count);
        foreach (string name in order) setups.Add(byName[name]);

        return ParseDbResult.Success(
            new SetupsDb { Schema = Schema, Setups = setups },
            skipped);
    }

    /// <summary>Pretty-printed (2-space) serialization — identical in localStorage and export files
    /// (legacy <c>serializeDb</c>, matching JS <c>JSON.stringify(db, null, 2)</c>).</summary>
    public static string SerializeDb(SetupsDb db) => JsonSerializer.Serialize(db, SerializeOptions);

    /// <summary>
    /// Pure upsert (legacy <c>upsertSetup</c>): returns a new db with the validated <paramref name="setup"/>
    /// replacing any same-(trimmed-)name entry. An object that doesn't validate leaves the db unchanged.
    /// </summary>
    public static SetupsDb UpsertSetup(SetupsDb db, JsonNode? setup)
    {
        SavedSetup? entry = ValidateSetup(setup);
        if (entry is null) return db;
        return UpsertSetup(db, entry);
    }

    /// <summary>Pure upsert with an already-validated entry (convenience for the Web snapshot path).</summary>
    public static SetupsDb UpsertSetup(SetupsDb db, SavedSetup entry)
    {
        var setups = new List<SavedSetup>(db.Setups.Count + 1);
        foreach (SavedSetup s in db.Setups)
            if (s.Name != entry.Name) setups.Add(s);
        setups.Add(entry);
        return new SetupsDb { Schema = Schema, Setups = setups };
    }

    /// <summary>
    /// Pure delete (legacy <c>deleteSetup</c>): returns a new db without the named entry (exact,
    /// trimmed match). A null/blank name can't name a stored setup — no-op (db returned unchanged).
    /// </summary>
    public static SetupsDb DeleteSetup(SetupsDb db, string? name)
    {
        string n = name?.Trim() ?? "";
        if (n.Length == 0) return db;

        var setups = new List<SavedSetup>(db.Setups.Count);
        foreach (SavedSetup s in db.Setups)
            if (s.Name != n) setups.Add(s);
        return new SetupsDb { Schema = Schema, Setups = setups };
    }

    /// <summary>
    /// Merge a restored backup into the existing db (legacy <c>mergeDb</c>): imported wins by name,
    /// existing setups absent from the import are preserved, imported entries keep their own
    /// <c>savedAt</c>. Pure. Expects <see cref="ParseDb"/>/<see cref="EmptyDb"/>-produced dbs (names
    /// trimmed and unique per side).
    /// </summary>
    public static MergeDbResult MergeDb(SetupsDb existing, SetupsDb imported)
    {
        int added = 0, updated = 0;
        var byName = new Dictionary<string, SavedSetup>();
        var order = new List<string>();

        foreach (SavedSetup s in existing.Setups)
        {
            if (!byName.ContainsKey(s.Name)) order.Add(s.Name);
            byName[s.Name] = s;
        }

        foreach (SavedSetup s in imported.Setups)
        {
            if (byName.ContainsKey(s.Name)) updated++;
            else { added++; order.Add(s.Name); }
            byName[s.Name] = s;
        }

        var setups = new List<SavedSetup>(order.Count);
        foreach (string name in order) setups.Add(byName[name]);

        return new MergeDbResult
        {
            Db = new SetupsDb { Schema = Schema, Setups = setups },
            Added = added,
            Updated = updated,
        };
    }

    // ---- helpers ----

    /// <summary>The JSON value's string content, or null if it isn't a JSON string (mirrors the JS
    /// <c>typeof x === "string"</c> guards used throughout validateSetup).</summary>
    private static string? AsStringOrNull(JsonNode? node)
    {
        if (node is JsonValue v && v.TryGetValue(out string? s)) return s;
        return null;
    }

    /// <summary>True if the JSON value is a number, yielding it as a double (mirrors JS
    /// <c>typeof schema === "number"</c>; a JSON string like "1" returns false).</summary>
    private static bool TryGetNumber(JsonValue value, out double number)
    {
        if (value.TryGetValue(out double d)) { number = d; return true; }
        number = 0;
        return false;
    }
}
