// Clipboard helper for the Copy-tune button — port of the legacy app.js copy handler
// (lines 722-735): try navigator.clipboard.writeText, and on failure fall back to a
// window.prompt so the user can still copy the text manually (e.g. on file:// where the
// async clipboard API is blocked). Returns true if the modern API succeeded, false if it
// fell back to the prompt.
export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch (e) {
    // clipboard may be blocked (insecure context / permissions) — fall back to a prompt.
    window.prompt("Copy your tune:", text);
    return false;
  }
}
