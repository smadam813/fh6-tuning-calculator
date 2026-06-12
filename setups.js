/* =====================================================================
   FH6 Tuning Calculator — saved-setups storage logic (pure, no DOM)
   Validates, serializes and merges the saved-setup database the UI
   keeps in localStorage and round-trips through JSON backup files.
   Dual-exported like tuning.js so node:test can require it directly.
   ===================================================================== */
(function () {
  "use strict";

  const STORAGE_KEY = "fh6-tuning.setups.v1";
  const SCHEMA = 1;

  function emptyDb() {
    return { schema: SCHEMA, setups: [] };
  }

  // Normalize an object into a storable setup entry, or null if it can't be
  // one. Requires a non-empty (trimmed) string name and an object of raw
  // field strings; units/goal/dials/savedAt get safe defaults. Unknown extra
  // keys are kept so a backup written by a newer app version survives a
  // round-trip through this one.
  function validateSetup(obj) {
    if (!obj || typeof obj !== "object" || Array.isArray(obj)) return null;
    if (typeof obj.name !== "string" || obj.name.trim() === "") return null;
    if (!obj.fields || typeof obj.fields !== "object" || Array.isArray(obj.fields)) return null;
    const entry = Object.assign({}, obj);
    entry.name = obj.name.trim();
    entry.units = obj.units === "metric" ? "metric" : "imperial";
    entry.goal = typeof obj.goal === "string" ? obj.goal : "";
    entry.dials = obj.dials && typeof obj.dials === "object" && !Array.isArray(obj.dials) ? obj.dials : {};
    entry.savedAt = typeof obj.savedAt === "string" ? obj.savedAt : "";
    return entry;
  }

  const API = { STORAGE_KEY, SCHEMA, emptyDb, validateSetup };
  if (typeof window !== "undefined") window.SETUPS = API;
  if (typeof module !== "undefined" && module.exports) module.exports = API;
})();
