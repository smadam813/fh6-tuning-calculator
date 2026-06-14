using Fh6Tuning.Core;
using Microsoft.JSInterop;

namespace Fh6Tuning.Web.Services;

/// <summary>
/// Severity of a <see cref="SetupsLoadResult"/> / <see cref="SetupsSaveResult"/> status note —
/// drives whether the UI shows the message in its error style (legacy
/// <c>setupStatus(msg, isError)</c>).
/// </summary>
public enum SetupsStatusKind { None, Info, Error }

/// <summary>A user-facing status note plus its severity. Empty <see cref="Message"/> means "no note".</summary>
public readonly record struct SetupsStatus(SetupsStatusKind Kind, string Message)
{
    public static readonly SetupsStatus None = new(SetupsStatusKind.None, "");
    public static SetupsStatus Error(string msg) => new(SetupsStatusKind.Error, msg);
    public static SetupsStatus Info(string msg) => new(SetupsStatusKind.Info, msg);
}

/// <summary>
/// Outcome of <see cref="SetupsStorage.LoadAsync"/>: the db (always a usable one — degrades to
/// <see cref="SetupsStore.EmptyDb"/> on any failure) plus an optional status note and the count of
/// entries dropped on parse (legacy <c>lastLoadSkipped</c>).
/// </summary>
public readonly record struct SetupsLoadResult(SetupsDb Db, SetupsStatus Status, int Skipped);

/// <summary>Outcome of <see cref="SetupsStorage.SaveAsync"/>: whether the write succeeded plus an
/// optional status note.</summary>
public readonly record struct SetupsSaveResult(bool Ok, SetupsStatus Status);

/// <summary>
/// localStorage interop for the saved-setups db. <b>The only thing here that isn't pure is the raw
/// localStorage get/set</b> — all validate / parse / serialize logic lives in
/// <see cref="SetupsStore"/> (Core). This wrapper reproduces the legacy
/// <c>loadSetupsDb</c>/<c>saveSetupsDb</c> degrade-to-empty-with-status behavior:
/// <list type="bullet">
///   <item>storage unavailable (private mode, blocked) on read → empty db + error status.</item>
///   <item>stored value unreadable (bad JSON / envelope) → empty db + error status.</item>
///   <item>some entries dropped on parse → the db that parsed + error status with the count.</item>
///   <item>missing key (never saved) → empty db, no status.</item>
///   <item>write throws (quota, blocked) → save fails + error status; the in-memory db is unaffected.</item>
/// </list>
/// </summary>
public sealed class SetupsStorage(IJSRuntime js)
{
    private readonly IJSRuntime _js = js;

    /// <summary>
    /// Load the db from localStorage, degrading to <see cref="SetupsStore.EmptyDb"/> on any failure.
    /// Never throws; the calculator keeps working regardless of storage state.
    /// </summary>
    public async Task<SetupsLoadResult> LoadAsync()
    {
        string? raw;
        try
        {
            raw = await _js.InvokeAsync<string?>("localStorage.getItem", SetupsStore.StorageKey);
        }
        catch (Exception)
        {
            // getItem itself threw — storage unavailable (e.g. blocked in private mode).
            return new SetupsLoadResult(
                SetupsStore.EmptyDb(),
                SetupsStatus.Error("Browser storage unavailable — setups won't persist."),
                0);
        }

        // Key never written — a clean first visit, not an error.
        if (raw is null) return new SetupsLoadResult(SetupsStore.EmptyDb(), SetupsStatus.None, 0);

        ParseDbResult res = SetupsStore.ParseDb(raw);
        if (!res.Ok)
        {
            return new SetupsLoadResult(
                SetupsStore.EmptyDb(),
                SetupsStatus.Error("Stored setups were unreadable — starting fresh."),
                0);
        }

        SetupsDb db = res.Db!;
        if (res.Skipped > 0)
        {
            int n = res.Skipped;
            string was = n == 1 ? "was" : "were";
            string s = n == 1 ? "" : "s";
            return new SetupsLoadResult(
                db,
                SetupsStatus.Error($"{n} stored setup{s} couldn't be kept and {was} dropped."),
                n);
        }

        return new SetupsLoadResult(db, SetupsStatus.None, 0);
    }

    /// <summary>
    /// Serialize and write the db to localStorage, degrading to a failed result + error status if the
    /// write throws (quota exceeded, blocked). Never throws.
    /// </summary>
    public async Task<SetupsSaveResult> SaveAsync(SetupsDb db)
    {
        try
        {
            string json = SetupsStore.SerializeDb(db);
            await _js.InvokeVoidAsync("localStorage.setItem", SetupsStore.StorageKey, json);
            return new SetupsSaveResult(true, SetupsStatus.None);
        }
        catch (Exception)
        {
            return new SetupsSaveResult(false, SetupsStatus.Error("Couldn't write browser storage — setups not saved."));
        }
    }
}
