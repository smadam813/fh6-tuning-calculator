# Technology Stack

**Analysis Date:** 2026-06-13

## Languages

**Primary:**
- JavaScript (ES2015+, vanilla / no transpilation) - Application logic (`app.js`, `tuning.js`, `setups.js`, `sweep.js`) and tests (`test/*.js`)

**Secondary:**
- HTML5 - Single-page UI shell (`index.html`)
- CSS3 - Styling, dark color scheme, responsive layout (`styles.css`)

## Runtime

**Environment:**
- Browser (client-side only) for the app — scripts loaded directly via `<script>` tags in `index.html`
- Node.js (for tests/tooling only) — modules dual-export via `module.exports` so `node:test` can `require()` them

**Package Manager:**
- None. No `package.json`, no lockfile.
- This is a deliberate zero-build, zero-dependency static app. `.gitignore` guards `node_modules/` "against future tooling."

## Frameworks

**Core:**
- None. No frontend framework (no React/Vue/etc.). Plain DOM manipulation in `app.js` (`window.TUNING`, `window.SETUPS` globals).

**Testing:**
- `node:test` (Node.js built-in test runner) - All suites in `test/`
- `node:assert/strict` - Assertions

**Build/Dev:**
- None. No bundler, transpiler, or task runner. Files are served as-is.

## Key Dependencies

**Critical:**
- None (zero third-party runtime dependencies). README states "all math is community-standard."

**Infrastructure:**
- None. No installed packages; the repo has no `node_modules/` and no manifest.

## Configuration

**Environment:**
- No environment variables. No `.env` files present.
- App configuration is entirely in-code (tuning constants/ranges in `tuning.js`; storage key in `setups.js`).

**Build:**
- No build config (no webpack/vite/rollup/tsconfig). `index.html` references local scripts directly.

## Module Loading

- App scripts loaded in dependency order in `index.html`: `tuning.js` → `setups.js` → `app.js` (lines 260-262).
- Each module uses a dual-export guard:
  - `if (typeof window !== "undefined") window.TUNING = API;`
  - `if (typeof module !== "undefined" && module.exports) module.exports = API;`
  This lets the same file work as a browser global and a Node-requirable module for tests.

## Platform Requirements

**Development:**
- Node.js (any modern version with `node:test`, i.e. Node 18+) to run the test suite.
- Any static file server or opening `index.html` directly to run the app.

**Production:**
- Any static host (no server-side runtime required). Pure static asset delivery.

---

*Stack analysis: 2026-06-13*
