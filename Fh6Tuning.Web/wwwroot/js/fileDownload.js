// File-download helper for the saved-setups "Export JSON" button — port of the legacy app.js
// export handler (lines 674-687): build a Blob from the serialized db, create an object URL,
// click a transient <a download> with a dated filename, then revoke the URL. Browser-only side
// effect; the dated filename + serialized JSON are produced in C# and passed in.
export function download(filename, mime, text) {
  const blob = new Blob([text], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  // give the click a tick to start before revoking (matches legacy setTimeout 1000ms).
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
