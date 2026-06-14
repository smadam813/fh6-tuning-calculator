using Microsoft.JSInterop;

namespace Fh6Tuning.Web.Services;

/// <summary>
/// Thin wrapper over <c>wwwroot/js/fileDownload.js</c> for the saved-setups "Export JSON" button.
/// Ports the legacy export handler (app.js:674-687): the dated filename + serialized JSON are built
/// in C#; this just hands them to a Blob → object-URL → transient-anchor download in the browser.
/// The module is lazily imported on first use and disposed with the component scope.
/// </summary>
public sealed class FileDownloadInterop : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public FileDownloadInterop(IJSRuntime js) => _js = js;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/fileDownload.js");

    /// <summary>
    /// Trigger a browser download of <paramref name="text"/> as <paramref name="filename"/>. Never
    /// throws — a JS-interop failure is swallowed (the export is best-effort, like the legacy handler).
    /// </summary>
    public async Task DownloadAsync(string filename, string text, string mime = "application/json")
    {
        try
        {
            var module = await ModuleAsync();
            await module.InvokeVoidAsync("download", filename, mime, text);
        }
        catch (Exception)
        {
            // download blocked / circuit gone — nothing actionable; the status line still reports success
            // intent (legacy never checked the result either).
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { /* circuit gone — nothing to dispose */ }
        }
    }
}
