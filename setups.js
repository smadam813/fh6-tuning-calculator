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
    Object.setPrototypeOf(entry, Object.prototype); // JSON can smuggle an own "__proto__" key through Object.assign
    entry.name = obj.name.trim();
    entry.units = obj.units === "metric" ? "metric" : "imperial";
    entry.goal = typeof obj.goal === "string" ? obj.goal : "";
    entry.dials = obj.dials && typeof obj.dials === "object" && !Array.isArray(obj.dials) ? obj.dials : {};
    entry.savedAt = typeof obj.savedAt === "string" ? obj.savedAt : "";
    return entry;
  }

  // Parse a JSON string (localStorage value or backup file) into a db.
  // -> { ok:true, db, skipped } with invalid entries dropped & counted, or
  // -> { ok:false, error } when the envelope itself is unusable.
  // A schema NEWER than ours is still read entry-by-entry (best effort)
  // rather than rejected.
  function parseDb(jsonString) {
    let raw;
    try {
      raw = JSON.parse(jsonString);
    } catch (e) {
      return { ok: false, error: "not valid JSON" };
    }
    if (!raw || typeof raw !== "object" || Array.isArray(raw)) return { ok: false, error: "not a setups backup (expected an object)" };
    if (typeof raw.schema !== "number" || raw.schema < 1) return { ok: false, error: "not a setups backup (bad schema)" };
    if (!Array.isArray(raw.setups)) return { ok: false, error: "not a setups backup (missing setups list)" };
    const byName = new Map(); // also dedupes by trimmed name: last entry wins
    let skipped = 0;
    for (const item of raw.setups) {
      const entry = validateSetup(item);
      if (!entry) { skipped++; continue; }
      if (byName.has(entry.name)) skipped++;
      byName.set(entry.name, entry);
    }
    return { ok: true, db: { schema: SCHEMA, setups: [...byName.values()] }, skipped };
  }

  // Pretty-printed both in localStorage and in export files (identical
  // format by design; size is irrelevant at this scale).
  function serializeDb(db) {
    return JSON.stringify(db, null, 2);
  }

  // NOTE: array position is meaningless throughout this module — the UI
  // sorts by savedAt. (parseDb dedupes in place; upsert appends; both fine.)
  // Pure: returns a new db with `setup` replacing any same-name entry.
  // An object that doesn't validate leaves the db unchanged.
  function upsertSetup(db, setup) {
    const entry = validateSetup(setup);
    if (!entry) return db;
    const setups = db.setups.filter((s) => s.name !== entry.name);
    setups.push(entry);
    return { schema: SCHEMA, setups };
  }

  // Pure: returns a new db without the named entry (exact, trimmed match).
  function deleteSetup(db, name) {
    const n = typeof name === "string" ? name.trim() : "";
    if (!n) return db; // non-strings/blank can't name a stored setup — no-op
    return { schema: SCHEMA, setups: db.setups.filter((s) => s.name !== n) };
  }

  // Merge a restored backup into the existing db: imported wins by name,
  // existing setups absent from the import are preserved, imported entries
  // keep their own savedAt. Pure; returns { db, added, updated }.
  // Expects parseDb/emptyDb-produced dbs (names trimmed and unique per side).
  function mergeDb(existing, imported) {
    let added = 0, updated = 0;
    const byName = new Map(existing.setups.map((s) => [s.name, s]));
    for (const s of imported.setups) {
      if (byName.has(s.name)) updated++;
      else added++;
      byName.set(s.name, s);
    }
    return { db: { schema: SCHEMA, setups: [...byName.values()] }, added, updated };
  }

  const API = { STORAGE_KEY, SCHEMA, emptyDb, validateSetup, parseDb, serializeDb, upsertSetup, deleteSetup, mergeDb };
  if (typeof window !== "undefined") window.SETUPS = API;
  if (typeof module !== "undefined" && module.exports) module.exports = API;
})();
