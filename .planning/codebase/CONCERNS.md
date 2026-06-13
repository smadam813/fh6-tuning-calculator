# Codebase Concerns

**Analysis Date:** 2026-06-13

## Context

This is a zero-build, zero-dependency static web app (vanilla JS, `file://`-capable).
There is no package manager, no bundler, and no runtime backend. All logic ships as
three global-namespace scripts loaded in order by `index.html`: `tuning.js` (pure
engine), `setups.js` (pure storage logic), `app.js` (DOM/UI glue). Tests run via
`node --test` against the dual-exported pure modules. Because there is no server,
the usual server-side security/perf concerns (auth, injection, DB load) do not apply —
the realistic concern surface is client-side: DOM injection (XSS), localStorage
handling, untested UI glue, and engine math fragility.

The repository already contains a deep calculation audit (`AUDIT.md`) and a Node
invariant sweep (`sweep.js`, 239,562 output checks, 0 errors). The math engine is
therefore the *least* risky part of the codebase and is treated as such below.

## Tech Debt

**Inconsistent HTML-escaping discipline in `renderCards`:**
- Issue: Some interpolated values are wrapped in `escapeHtml(...)` (e.g. `why.formula`,
  the slider-changes panel, error rows), but sibling interpolations in the same render
  path are not. `app.js:224-225` (`summaryStrip` chips, `c.k`/`c.v`), `app.js:233`
  (`row.k`/`row.v`), and `app.js:238` (`why.text`) are injected into `innerHTML` raw.
- Files: `app.js:222-241`
- Impact: Currently safe — every value reaching these spots originates in `tuning.js`
  and is built from fixed enum strings (drivetrain, tireCompound, piClass from
  `<select>` options) plus computed numbers, none of which are free-text user input.
  But the mixed convention is a latent trap: a future change that routes any
  user-entered string (e.g. the setup name, or a new free-text field) into `why.text`
  or a summary chip would become a stored-XSS vector with no compile-time warning.
- Fix approach: Make escaping uniform. Either run *every* dynamic string through
  `escapeHtml` at the point of interpolation, or move card rendering to DOM-node
  construction (`textContent`) the way `renderSetupList` already does. Add a short
  code comment documenting the "engine output is trusted, user input is not" rule.

**`escapeHtml` does not escape quotes:**
- Issue: `escapeHtml` (`app.js:533-534`) only replaces `&`, `<`, `>`. It is safe for
  text-content contexts but would be unsafe if its output were ever placed inside an
  HTML *attribute* value (e.g. `title="${escapeHtml(x)}"`), where a `"` could break out.
- Files: `app.js:533-534`
- Impact: No current misuse — all call sites are element-text contexts. Latent risk
  if a future attribute interpolation reuses this helper.
- Fix approach: Either extend the replacement map to include `"` (`&quot;`) and `'`
  (`&#39;`), or rename/comment the helper to make its text-only contract explicit.

**Global-namespace module loading with implicit ordering:**
- Issue: Modules communicate via `window.TUNING`, `window.SETUPS`, and a fixed
  `<script>` order in `index.html:260-262`. There is no import graph or error if a
  script fails to load (app.js partially guards: `wireSetups` early-returns when
  `SETUPS` is absent, `app.js:641`).
- Files: `index.html:260-262`, `tuning.js:948-949`, `setups.js:105-106`
- Impact: Fragile to reordering; a typo or load failure degrades silently rather than
  erroring loudly. Acceptable for the project's no-build constraint, but it caps how
  large the codebase can grow before this becomes painful.
- Fix approach: Acceptable as-is for current scale. If the app grows, consider native
  ES modules (`<script type="module">`) which still require no build step and give real
  import errors.

## Known Bugs

No functional bugs identified. `AUDIT.md` documents previously-found math issues, all
marked FIXED, and `sweep.js` reports 0 invariant violations across 3,969 `compute()`
calls. The example-based suite (`test/`, ~91 tests via `node --test`) passes.

## Security Considerations

**Client-side DOM injection (XSS) — primary surface:**
- Risk: Stored XSS via saved-setup data (localStorage) or imported backup JSON.
- Files: `app.js` render paths; `setups.js` validation.
- Current mitigation: Strong. Setup names render through `document.createElement` +
  `textContent` / `option.value` (`app.js:617-632`), never `innerHTML`. Saved field
  values are written via `el.value` and gated to existing inputs inside `.inputs` and
  outside `#setupsBlock` (`app.js:605-609`). `setups.js` validates the envelope and
  every entry, drops invalid ones with a count, and explicitly neutralizes
  `__proto__` prototype-pollution smuggling (`setups.js:29`). User free-text never
  reaches a raw `innerHTML` sink today.
- Recommendations: Keep it that way — see the escaping-discipline debt above. Consider
  a one-line CSP `<meta>` in `index.html` as defense-in-depth (note: some CSP
  directives behave differently under `file://`, which the app supports).

**localStorage availability and corruption:**
- Risk: Private-mode / disabled storage throwing on access; corrupted or
  newer-schema stored JSON.
- Files: `app.js:550-578` (`loadSetupsDb`/`saveSetupsDb`), `setups.js:43-62` (`parseDb`).
- Current mitigation: Strong. All access is try/caught and degrades to an empty DB with
  a user-visible status note; the calculator keeps working. `parseDb` is best-effort
  (reads newer schemas entry-by-entry, dedupes by name, counts skipped). No concern.

**No secrets, no network, no auth:** Static client-only app — no credentials, API keys,
env files, or outbound requests exist in the repo. Not applicable.

## Performance Bottlenecks

**Full re-render on every input event:**
- Problem: Every `input` event calls `refresh()` which recomputes all goals and rebuilds
  large `innerHTML` strings for the output cards and the all-goals compare table.
- Files: `app.js:712-714` (input listeners), `renderCards`/compare render (`app.js:222+`,
  `app.js:312+`).
- Cause: Synchronous recompute + full string-rebuild per keystroke/slider-tick.
- Improvement path: Negligible at this scale (a handful of small objects, a few hundred
  DOM nodes) and not user-perceptible. If the compare table or goal count grows, debounce
  `refresh()` or diff-render. Low priority — flagged only for completeness.

## Fragile Areas

**`app.js` UI glue (largest file, untested):**
- Files: `app.js` (741 lines).
- Why fragile: It is the only module with no automated tests (the suite covers
  `tuning.js` and `setups.js`, both pure; `test/fixtures.js` only references the engine).
  It owns unit conversion, the live-binding wiring, the compare table, copy-to-clipboard,
  and all setups UI — exactly the integration-heavy logic most prone to regressions.
- Safe modification: Manually exercise units toggle, save/load/delete/import/export, and
  the compare table after any change here, since no test will catch a break. Keep all
  business/math logic in `tuning.js` (testable) and treat `app.js` as a thin shell.
- Test coverage: No coverage for DOM behavior, unit conversion round-trips, or the
  setups *UI* flow (the pure `setups.js` logic is well-tested in `test/setups.test.js`,
  but the wiring in `app.js:640-706` is not).

**Engine has no internal error handling:**
- Files: `tuning.js` (no try/catch; `tuning.js:948-949` exports).
- Why fragile: `compute()` trusts its input shape and relies entirely on upstream
  validation/coercion in `app.js`. A malformed input object (e.g. from a future caller
  or a hand-edited import that bypasses field coercion) could produce `NaN` outputs
  rather than a clean error.
- Safe modification: Preserve the `app.js` input-building/coercion layer; don't call
  `TUNING.compute` with raw, unvalidated objects. The `sweep.js` invariant harness is
  the safety net — re-run `node sweep.js` after any engine change.
- Test coverage: Excellent for valid inputs (sweep + example suite); thin for
  deliberately malformed input objects.

## Scaling Limits

Not applicable in the traditional sense — single-user, client-only, no backend. The only
"scale" axis is the saved-setups list, which lives entirely in one localStorage key
(`fh6-tuning.setups.v1`) serialized as pretty-printed JSON (`setups.js:66-68`). Practical
ceiling is the browser's ~5 MB per-origin localStorage quota; at realistic setup sizes
this allows thousands of saved tunes. A write that exceeds quota is caught and surfaced as
a status message (`app.js:572-578`), so the failure mode is graceful.

## Dependencies at Risk

None. The project has zero runtime and zero build dependencies (no `package.json`,
`node_modules` is `.gitignore`'d defensively). Tests use only Node's built-in
`node:test` / `node:assert`. There is no supply-chain surface to track. This is a
deliberate and healthy posture for this project.

## Missing Critical Features

No blocking gaps for the app's stated purpose. Minor opportunities only:
- No CSP header/meta as defense-in-depth (see Security).
- No automated CI run of `node --test` / `node sweep.js` (no `.github/` workflow);
  correctness currently relies on contributors running tests manually. Adding a
  minimal CI workflow would protect the well-built test suite from silent rot.

## Test Coverage Gaps

**`app.js` (UI layer) — entirely untested:**
- What's not tested: unit conversion (`setUnits`/imperial⇄metric round-trips), live
  input binding, the all-goals compare table render, copy-to-clipboard, and the setups
  *UI* flow (save/load/delete/import/export wiring).
- Files: `app.js` (esp. `wireSetups` `app.js:640-706`, `applySetup` `app.js:600-615`,
  unit conversion helpers).
- Risk: A regression in conversion or setups wiring ships unnoticed; only the pure
  math/storage layers have a safety net.
- Priority: Medium. Hardest to test (DOM-dependent) but also the most integration-prone.
  A lightweight jsdom or Playwright smoke test of the save→reload→load cycle would close
  the highest-value gap.

**Engine malformed-input handling:**
- What's not tested: `TUNING.compute` behavior on structurally invalid input objects.
- Files: `tuning.js`.
- Risk: `NaN`/range escapes if a future caller bypasses `app.js` coercion.
- Priority: Low. Upstream coercion makes this unreachable in the current app; flagged for
  defensive robustness if the engine is reused elsewhere.

---

*Concerns audit: 2026-06-13*
