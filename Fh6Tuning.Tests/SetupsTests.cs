using System.Text.Json.Nodes;
using Fh6Tuning.Core;

namespace Fh6Tuning.Tests;

/// <summary>
/// SETUPS TESTS — the pure saved-Car-Setups storage logic (the C# port of
/// <c>legacy/test/setups.test.js</c>): envelope/entry validation, serialize↔parse round-trips,
/// name-keyed upsert/delete, and backup-merge semantics (imported wins by name). Operates on
/// <see cref="JsonNode"/> exactly as <see cref="SetupsStore"/> does.
/// </summary>
public sealed class SetupsTests
{
    // A complete, valid setup entry as a JsonObject; override any key via `over`.
    private static JsonObject Entry(Action<JsonObject>? over = null)
    {
        var obj = new JsonObject
        {
            ["name"] = "Test car",
            ["savedAt"] = "2026-06-12T00:00:00.000Z",
            ["units"] = "imperial",
            ["goal"] = "Circuit",
            ["dials"] = new JsonObject { ["handlingBias"] = "0", ["overallStiffness"] = "0" },
            ["fields"] = new JsonObject { ["drivetrain"] = "RWD", ["power"] = "400", ["torque"] = "" },
        };
        over?.Invoke(obj);
        return obj;
    }

    /* ---------------- emptyDb ---------------- */
    [Fact]
    public void EmptyDb_V1EnvelopeNoSetups()
    {
        var db = SetupsStore.EmptyDb();
        Assert.Equal(1, db.Schema);
        Assert.Empty(db.Setups);
    }

    /* ---------------- validateSetup ---------------- */
    [Fact]
    public void ValidateSetup_AcceptsCompleteEntry_TrimsName()
    {
        var e = SetupsStore.ValidateSetup(Entry(o => o["name"] = "  Test car  "));
        Assert.NotNull(e);
        Assert.Equal("Test car", e!.Name);
        Assert.Equal("imperial", e.Units);
        Assert.Equal("Circuit", e.Goal);
        Assert.Equal("RWD", (string?)e.Fields["drivetrain"]);
        Assert.Equal("400", (string?)e.Fields["power"]);
        Assert.Equal("", (string?)e.Fields["torque"]);
    }

    [Fact]
    public void ValidateSetup_RejectsWithoutUsableNameOrFields()
    {
        Assert.Null(SetupsStore.ValidateSetup(null));
        Assert.Null(SetupsStore.ValidateSetup(JsonValue.Create("nope")));
        Assert.Null(SetupsStore.ValidateSetup(Entry(o => o["name"] = "")));
        Assert.Null(SetupsStore.ValidateSetup(Entry(o => o["name"] = "   ")));
        Assert.Null(SetupsStore.ValidateSetup(Entry(o => o["name"] = 7)));
        Assert.Null(SetupsStore.ValidateSetup(Entry(o => o["fields"] = "nope")));
        Assert.Null(SetupsStore.ValidateSetup(Entry(o => o["fields"] = new JsonArray("nope"))));
        Assert.Null(SetupsStore.ValidateSetup(Entry(o => o["fields"] = null)));
        Assert.Null(SetupsStore.ValidateSetup(new JsonArray()));
        Assert.Null(SetupsStore.ValidateSetup(Entry(o => o["fields"] = 7)));
    }

    [Fact]
    public void ValidateSetup_DefaultsMissingFieldsSafely()
    {
        var e = SetupsStore.ValidateSetup(new JsonObject { ["name"] = "Bare", ["fields"] = new JsonObject() });
        Assert.NotNull(e);
        Assert.Equal("imperial", e!.Units);
        Assert.Equal("", e.Goal);
        Assert.Empty(e.Dials);
        Assert.Equal("", e.SavedAt);
        Assert.Equal("imperial", SetupsStore.ValidateSetup(Entry(o => o["units"] = "stone"))!.Units);
        Assert.Equal("metric", SetupsStore.ValidateSetup(Entry(o => o["units"] = "metric"))!.Units);
    }

    [Fact]
    public void ValidateSetup_PreservesUnknownExtraKeys()
    {
        var e = SetupsStore.ValidateSetup(Entry(o => o["futureThing"] = new JsonObject { ["x"] = 1 }));
        Assert.NotNull(e);
        Assert.True(e!.Extra.ContainsKey("futureThing"));
        // round-trips through serialize so the extra key survives
        string json = SetupsStore.SerializeDb(new SetupsDb { Schema = 1, Setups = new[] { e } });
        Assert.Contains("futureThing", json);
    }

    /* ---------------- serializeDb / parseDb ---------------- */
    [Fact]
    public void SerializeDb_ParseDb_RoundTripsLosslessly()
    {
        var db = new SetupsDb { Schema = 1, Setups = new[] { SetupsStore.ValidateSetup(Entry())! } };
        var res = SetupsStore.ParseDb(SetupsStore.SerializeDb(db));
        Assert.True(res.Ok);
        Assert.Equal(0, res.Skipped);
        // compare by re-serializing both sides (deepEqual analog)
        Assert.Equal(SetupsStore.SerializeDb(db), SetupsStore.SerializeDb(res.Db!));
    }

    [Fact]
    public void ParseDb_RejectsGarbageAndWrongEnvelopes()
    {
        foreach (var bad in new[] { "{nope", "42", "\"str\"", "[]", "null",
            "{\"setups\":[]}",
            "{\"schema\":\"1\",\"setups\":[]}",
            "{\"schema\":0,\"setups\":[]}",
            "{\"schema\":1}",
            "{\"schema\":1,\"setups\":{}}" })
        {
            var res = SetupsStore.ParseDb(bad);
            Assert.False(res.Ok, $"expected reject: {bad}");
            Assert.NotNull(res.Error);
        }
    }

    [Fact]
    public void ParseDb_DropsInvalidEntries_KeepsValidOnes()
    {
        var raw = new JsonObject
        {
            ["schema"] = 1,
            ["setups"] = new JsonArray(Entry(), new JsonObject { ["name"] = "" }, null, Entry(o => o["name"] = "Second")),
        }.ToJsonString();
        var res = SetupsStore.ParseDb(raw);
        Assert.True(res.Ok);
        Assert.Equal(2, res.Skipped);
        Assert.Equal(new[] { "Test car", "Second" }, res.Db!.Setups.Select(s => s.Name));
    }

    [Fact]
    public void ParseDb_ToleratesFutureSchema_ReadsEntryByEntry()
    {
        var raw = new JsonObject
        {
            ["schema"] = 2,
            ["setups"] = new JsonArray(Entry(o => o["newKey"] = "kept")),
        }.ToJsonString();
        var res = SetupsStore.ParseDb(raw);
        Assert.True(res.Ok);
        Assert.Equal(1, res.Db!.Schema); // normalized to the schema this app writes
        Assert.True(res.Db.Setups[0].Extra.ContainsKey("newKey"));
    }

    [Fact]
    public void ValidateSetup_NeutralizesSmuggledProtoKey()
    {
        // C# has no prototype to pollute; a "__proto__" key is preserved as ordinary extra data,
        // which is harmless. The entry still validates and carries no injected member.
        var raw = JsonNode.Parse("{\"name\":\"x\",\"fields\":{},\"__proto__\":{\"evil\":true}}");
        var e = SetupsStore.ValidateSetup(raw);
        Assert.NotNull(e);
        Assert.Equal("x", e!.Name);
    }

    [Fact]
    public void ParseDb_HandlesNonStringInputWithoutThrowing()
    {
        var res = SetupsStore.ParseDb(null);
        Assert.False(res.Ok);
    }

    [Fact]
    public void ParseDb_DedupesSameNameEntries_LastWins_CountsDrops()
    {
        var raw = new JsonObject
        {
            ["schema"] = 1,
            ["setups"] = new JsonArray(
                Entry(o => { o["name"] = "Dup"; o["fields"] = new JsonObject { ["power"] = "100" }; }),
                Entry(o => { o["name"] = " Dup "; o["fields"] = new JsonObject { ["power"] = "999" }; }),
                Entry(o => o["name"] = "Other")),
        }.ToJsonString();
        var res = SetupsStore.ParseDb(raw);
        Assert.True(res.Ok);
        Assert.Equal(1, res.Skipped);
        Assert.Equal(new[] { "Dup", "Other" }, res.Db!.Setups.Select(s => s.Name));
        var dup = res.Db.Setups.First(s => s.Name == "Dup");
        Assert.Equal("999", (string?)dup.Fields["power"]);
    }

    /* ---------------- upsertSetup / deleteSetup ---------------- */
    [Fact]
    public void UpsertSetup_AddsNew_ReplacesExistingByTrimmedName()
    {
        var db = SetupsStore.EmptyDb();
        db = SetupsStore.UpsertSetup(db, Entry());
        db = SetupsStore.UpsertSetup(db, Entry(o => o["name"] = "Other"));
        Assert.Equal(2, db.Setups.Count);
        db = SetupsStore.UpsertSetup(db, Entry(o => { o["name"] = "  Test car "; o["fields"] = new JsonObject { ["power"] = "900" }; }));
        Assert.Equal(2, db.Setups.Count);
        var tc = db.Setups.First(s => s.Name == "Test car");
        Assert.Equal("900", (string?)tc.Fields["power"]);
        Assert.Single((IEnumerable<KeyValuePair<string, JsonNode?>>)tc.Fields);
    }

    [Fact]
    public void UpsertSetup_IgnoresInvalidEntry()
    {
        var db = SetupsStore.UpsertSetup(SetupsStore.EmptyDb(), new JsonObject { ["name"] = "", ["fields"] = new JsonObject() });
        Assert.Empty(db.Setups);
    }

    [Fact]
    public void Upsert_Delete_Merge_DoNotMutateInputDbs()
    {
        var before = SetupsStore.UpsertSetup(SetupsStore.EmptyDb(), Entry());
        string snapshot = SetupsStore.SerializeDb(before);
        SetupsStore.UpsertSetup(before, Entry(o => o["name"] = "Another"));
        SetupsStore.DeleteSetup(before, "Test car");
        SetupsStore.MergeDb(before, new SetupsDb
        {
            Schema = 1,
            Setups = new[] { SetupsStore.ValidateSetup(Entry(o => { o["name"] = "Test car"; o["fields"] = new JsonObject { ["power"] = "777" }; }))! },
        });
        Assert.Equal(snapshot, SetupsStore.SerializeDb(before));
    }

    [Fact]
    public void DeleteSetup_RemovesExactlyNamedEntry_Trimmed()
    {
        var db = SetupsStore.EmptyDb();
        db = SetupsStore.UpsertSetup(db, Entry());
        db = SetupsStore.UpsertSetup(db, Entry(o => o["name"] = "Keep me"));
        db = SetupsStore.DeleteSetup(db, " Test car ");
        Assert.Equal(new[] { "Keep me" }, db.Setups.Select(s => s.Name));
        Assert.Single(SetupsStore.DeleteSetup(db, "ghost").Setups);
    }

    [Fact]
    public void DeleteSetup_IgnoresNonStringNames()
    {
        var db = SetupsStore.EmptyDb();
        db = SetupsStore.UpsertSetup(db, Entry(o => o["name"] = "undefined"));
        db = SetupsStore.UpsertSetup(db, Entry(o => o["name"] = "null"));
        Assert.Equal(2, SetupsStore.DeleteSetup(db, null).Setups.Count);
        Assert.Equal(2, SetupsStore.DeleteSetup(db, "  ").Setups.Count);
    }

    /* ---------------- mergeDb ---------------- */
    [Fact]
    public void MergeDb_ImportedWinsByName_UnrelatedExistingSurvive()
    {
        var existing = SetupsStore.EmptyDb();
        existing = SetupsStore.UpsertSetup(existing, Entry(o => { o["name"] = "A"; o["fields"] = new JsonObject { ["power"] = "100" }; }));
        existing = SetupsStore.UpsertSetup(existing, Entry(o => { o["name"] = "B"; o["fields"] = new JsonObject { ["power"] = "200" }; }));
        var imported = new SetupsDb
        {
            Schema = 1,
            Setups = new[]
            {
                SetupsStore.ValidateSetup(Entry(o => { o["name"] = "B"; o["fields"] = new JsonObject { ["power"] = "999" }; o["savedAt"] = "2026-01-01T00:00:00.000Z"; }))!,
                SetupsStore.ValidateSetup(Entry(o => { o["name"] = "C"; o["fields"] = new JsonObject { ["power"] = "300" }; }))!,
            },
        };
        var merge = SetupsStore.MergeDb(existing, imported);
        Assert.Equal(1, merge.Added);
        Assert.Equal(1, merge.Updated);
        Assert.Equal(new[] { "A", "B", "C" }, merge.Db.Setups.Select(s => s.Name).OrderBy(n => n));
        var b = merge.Db.Setups.First(s => s.Name == "B");
        Assert.Equal("999", (string?)b.Fields["power"]);
        Assert.Equal("2026-01-01T00:00:00.000Z", b.SavedAt);
    }

    [Fact]
    public void MergeDb_EmptyImportChangesNothing()
    {
        var existing = SetupsStore.UpsertSetup(SetupsStore.EmptyDb(), Entry());
        var merge = SetupsStore.MergeDb(existing, SetupsStore.EmptyDb());
        Assert.Equal(0, merge.Added + merge.Updated);
        Assert.Equal(SetupsStore.SerializeDb(existing), SetupsStore.SerializeDb(merge.Db));
    }
}
