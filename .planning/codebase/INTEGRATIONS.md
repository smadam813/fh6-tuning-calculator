# External Integrations

**Analysis Date:** 2026-06-13

## APIs & External Services

**None.**
- No outbound HTTP calls. No `fetch`, `XMLHttpRequest`, or third-party SDK usage anywhere in `app.js`, `tuning.js`, `setups.js`, or `sweep.js`.
- The app is fully self-contained: it computes Forza Horizon tuning values locally from user input.

## Data Storage

**Databases:**
- None. No server or remote database.

**Browser Storage (local persistence):**
- `window.localStorage` - Stores saved car setups as a JSON "db".
  - Access wrapped defensively in `app.js` (lines ~547-574) to tolerate private-mode / unreadable storage.
  - Storage key: `SETUPS.STORAGE_KEY` (defined in `setups.js`).
  - Serialization: `SETUPS.serializeDb()` / parse helpers in `setups.js` (pretty-printed JSON, identical format in storage and backups).

**File Storage:**
- Local filesystem only, via browser file download/upload:
  - Export: `Blob` + `URL.createObjectURL` generates a `fh6-setups-YYYY-MM-DD.json` download (`app.js` ~676-681).
  - Import/restore: JSON backup files parsed and merged into the db (`setups.js` `mergeDb`, ~89-96).

**Caching:**
- None (beyond `localStorage` persistence above).

## Authentication & Identity

**None.**
- No login, no auth provider, no user accounts. The app is anonymous and client-only.

## Monitoring & Observability

**Error Tracking:**
- None. No Sentry/analytics/telemetry.

**Logs:**
- No structured logging. User-facing status/errors surfaced in the UI; storage failures handled gracefully in `app.js`.

## CI/CD & Deployment

**Hosting:**
- Not configured in-repo (static app — deployable to any static host).

**CI Pipeline:**
- None detected. No `.github/workflows`, CI config, or deployment scripts.

## Environment Configuration

**Required env vars:**
- None.

**Secrets location:**
- None. No secrets, tokens, or credentials anywhere in the repo.

## Webhooks & Callbacks

**Incoming:**
- None.

**Outgoing:**
- None.

## User Interactions (browser APIs used)

- `window.confirm` / `window.prompt` - Overwrite/delete confirmations and copy-tune fallback (`app.js` ~648, 669, 733).
- `Blob` / `URL.createObjectURL` - JSON backup export.
- `localStorage` - Setup persistence.

---

*Integration audit: 2026-06-13*
