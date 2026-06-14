using Microsoft.JSInterop;

namespace Fh6Tuning.Web.Services;

/// <summary>
/// Thin wrapper over <c>wwwroot/js/clipboard.js</c> for the Copy-tune button. Ports the legacy
/// clipboard handler (app.js:722-735): write to the clipboard via the async API, falling back to a
/// <c>window.prompt</c> when it's blocked (e.g. insecure context). The module is lazily imported on
/// first use and disposed with the component scope.
/// </summary>
public sealed class ClipboardInterop : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public ClipboardInterop(IJSRuntime js) => _js = js;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/clipboard.js");

    /// <summary>
    /// Copy <paramref name="text"/> to the clipboard. Returns <c>true</c> if the async clipboard API
    /// succeeded (the button can flash "Copied ✓"); <c>false</c> if it fell back to the prompt.
    /// Never throws — a JS-interop failure degrades to <c>false</c>.
    /// </summary>
    public async Task<bool> CopyAsync(string text)
    {
        try
        {
            var module = await ModuleAsync();
            return await module.InvokeAsync<bool>("copyText", text);
        }
        catch (Exception)
        {
            return false;
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
