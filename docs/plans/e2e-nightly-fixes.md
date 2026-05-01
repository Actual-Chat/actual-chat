# Plan: Fix E2E nightly failures (post-signIn-fix)

**Source:** Nightly run https://github.com/Actual-Chat/actual-chat/actions/runs/25172477238
**Status:** signIn helper fix from `871995fde` held — surfaced new failures further down.
**Prior context:** Earlier run https://github.com/Actual-Chat/actual-chat/actions/runs/25145158405 failed inside `signIn` (stacked `Modal-ConfirmModal` overlays). That was fixed in `871995fde` by targeting the "Register new account?" modal explicitly and removing the redundant verify-button click.

## Current failures

### TS E2E — 9 failures across 4 files

| Test | File:line | Error | Cause |
|---|---|---|---|
| `avatar editing > should create a new avatar and set name and bio` | `avatar-edit.test.ts:108` | `addBtn.click()` 30s timeout | `.onboarding-tutorial.tutorial-1` (TranscriptionTutorialStep) inside fresh `Modal-OnboardingModal+Model-5` intercepts. Bare `.click()`, no `clickResilient`. |
| `avatar editing > should edit an existing avatar name` | `avatar-edit.test.ts:144` | `accountTab.click()` 30s timeout | `modal-chrome-overlay` from `Modal-OnboardingModal+Model-2` intercepts. Bare `.click()`, no `clickResilient`. |
| `avatar editing > should persist avatar changes after page reload` | `avatar-edit.test.ts:203` | Test timed out 60s | Cascade — same overlay issue likely affects `accountTab.click()` (line 209). |
| `mention search > should show mention list when typing @` | `mention-search.test.ts:46` (`ensureEditorReady`) | `#message-input .editor-content[contenteditable="true"]` not visible (10s) | Editor never appears after navigation. |
| `mention search` (second case) | `mention-search.test.ts` | Same as above | Same. |
| `sign-in and send message > should navigate to a chat and see the message input` | `signin-and-message.test.ts:69` | `messageInput.waitFor` 15s timeout | Editor never appears after `/chat/the-actual-one`. |
| `sign-in and send message > should send a message and see it appear` | `signin-and-message.test.ts:72` | Test timed out 30s | Cascade from prior. |
| `SVG avatar upload > should upload SVG picture in New Chat modal and convert to PNG` | `svg-avatar-upload.test.ts:191` | `expect(imgSrc).toMatch(/\.png/)` got `blob:http://localhost:7080/…` | SVG→PNG conversion not yet complete when the assertion reads `src`. |

### C# nightly — 2 failures (separate concern)

`tests/Chat.UI.Blazor.IntegrationTests/TranslationUITest.cs:60` and `:388` — `Expected isVisible to be True, but found False`. Unrelated to the E2E patterns above.

## Root cause patterns

1. **OnboardingModal re-mounts after `skipOnboarding` returns.**
   `OnboardingUI.ResetOnboarding(false)` (`src/dotnet/UI.Blazor.App/Services/OnboardingUI/OnboardingUI.cs:126`) marks all tutorial steps complete via `UserSettings.Set(...)` and calls `_lastModalRef?.Close(true)`. Propagation is async — between the helper returning and the next click, a fresh `Modal-OnboardingModal+Model-N` can render with a new ID. The helper's `display: none` only targets currently-rendered nodes; new ones render normally.

2. **`waitForChatReady` exits too early.**
   `tests/ts/e2e/helpers.ts:126` waits for ANY landmark (editor, "Join this chat", "Join anonymously", or signin button). On `the-actual-one` the editor selector matches but Blazor may unmount/remount it during chat load — the test races forward and the subsequent `messageInput.waitFor` fails.

3. **`svg-avatar-upload` reads `src` before conversion.**
   The chat picture starts as a `blob:` URL (the just-selected SVG) and is replaced with a server `.png` URL after the upload completes. Test reads at line 189–191 before the swap.

## Proposed fixes

### Avatar tests — wrap remaining bare clicks in `clickResilient`
`avatar-edit.test.ts` already defines `clickResilient` (lines 50–65) but doesn't use it for first-click paths.

- Line 100 (`accountTab.click()` in test 1)
- Line 108 (`addBtn.click()` in test 1)
- Line 144 (`accountTab.click()` in test 2) — already uses bare; the surrounding `if-isVisible` is racy too
- Line 209, 246 (`accountTab.click()` in test 3)
- Line 151, 216, 252 (`okBtn.click()` — bubble dismissal, lower priority)

### `signin-and-message` & `mention-search` — make message-input wait resilient
After `waitForChatReady` + `skipOnboarding`:
- Poll for `#message-input .editor-content[contenteditable="true"]` with retry interleaved with `skipOnboarding`, similar to `clickResilient` but for `waitFor`.
- Alternatively: extend `waitForChatReady` to require the editor specifically when the test expects it (rename or add `waitForEditor`).

### `svg-avatar-upload` — poll for `.png` src instead of single read
Replace the one-shot `getAttribute('src')` at line 189 with a polling expect:

```ts
await expect.poll(async () => await chatPic.getAttribute('src') ?? '',
    { timeout: 10_000, intervals: [200, 500, 1000] }).toMatch(/\.png/);
```

### Optional: harden `skipOnboarding` to handle re-mounts
After `resetOnboarding(false)`, briefly poll until no `[id^="Modal-OnboardingModal"]` is rendered for ~500 ms (settled), or until a 5 s budget elapses. This addresses the async propagation race at the source instead of per-test.

### TranslationUITest (separate task)
Inspect `tests/Chat.UI.Blazor.IntegrationTests/TranslationUITest.cs:60` and `:388` — likely either a backend translation flag/setting changed, or the awaited UI element name changed. Out of scope for the TS E2E pass.

## Open questions

- Are the chat-editor failures specific to CI's managed-server slowness, or reproducible locally? Worth checking with `AC_E2E_SERVER=managed npm run test:e2e -- signin-and-message` before adding retry loops.
- Should `the-actual-one` chat be a fixture seeded by `global-setup.ts` rather than relying on its existence?

---

## Phased execution

Each phase is independently shippable. Run order is recommended (Phase 0 reduces churn in 1–3) but not required. Phases 1–4 can each land as a separate commit; Phase 0 is a helper-only commit.

### Phase 0 — Harden `skipOnboarding` (foundational, optional)

**Goal:** Eliminate the re-mount race at the source so per-test workarounds shrink in scope.

**File:** `tests/ts/e2e/helpers.ts:153–205`

**Changes:**
- After the existing dismiss loop exits, add a "settled" check: poll up to ~3s and require `[id^="Modal-OnboardingModal"]` to remain absent for ~500ms before returning.
- Keep the `display: none` overlay scrub (still needed for already-rendered nodes between resets).

**Verification:**
- `npm run test:e2e -- avatar-edit signin-and-message mention-search` (locally and via `AC_E2E_SERVER=managed`).
- Compare flake rate against current branch; should not regress.

**Exit criteria:** All three suites' onboarding-related click interceptions in CI logs disappear or drop to zero across 3 consecutive runs.

**Risk:** Polling for absence increases per-test latency by up to 500ms. Acceptable.

---

### Phase 1 — `avatar-edit.test.ts` (3 failing tests)

**Goal:** Stop bare `.click()` calls from being intercepted by `Modal-OnboardingModal+Model-N` re-mounts.

**File:** `tests/ts/e2e/avatar-edit.test.ts`

**Changes:**
- Lines 100, 144, 209, 246 — replace `accountTab.click()` + `waitForTimeout(1000)` with `clickResilient(page, accountTab)`.
- Line 108 — replace `addBtn.click()` with `clickResilient(page, addBtn)`.
- Lines 151, 216, 252 — wrap `okBtn.click()` (bubble dismissal) in `clickResilient` for consistency. Lower priority but cheap.
- Keep the `if isVisible(...)` guards; they short-circuit when the tab/button isn't there.

**Verification:**
```bash
npx vitest run tests/ts/e2e/avatar-edit.test.ts --config vitest.config.e2e.ts
```
Run 3× locally to catch the race; all 3 tests must pass each run.

**Exit criteria:**
- `should create a new avatar and set name and bio` — passes
- `should edit an existing avatar name` — passes
- `should persist avatar changes after page reload` — passes

**Risk:** `clickResilient` adds up to 4×5s per click on retry. If a real bug surfaces, lower the attempt count locally to fail fast.

---

### Phase 2 — `signin-and-message.test.ts` (2 failing tests)

**Goal:** Survive the editor remount that happens after `the-actual-one` finishes loading.

**Files:**
- `tests/ts/e2e/helpers.ts` — add `waitForEditor(page, timeout)` that polls for `#message-input .editor-content[contenteditable="true"]` with interleaved `skipOnboarding` calls (same shape as `clickResilient`, but for `waitFor`).
- `tests/ts/e2e/signin-and-message.test.ts:54–70` — replace `messageInput.waitFor(...)` at line 69 with `await waitForEditor(page)`.
- Keep the existing `waitForChatReady` call at line 56 (still useful as a coarse landmark).

**Verification:**
```bash
npx vitest run tests/ts/e2e/signin-and-message.test.ts --config vitest.config.e2e.ts
```

**Exit criteria:**
- `should navigate to a chat and see the message input` — passes
- `should send a message and see it appear` — passes (cascade fix)

**Risk:** Polling masks legitimate "editor never renders" bugs. Mitigate with a 30s ceiling and a clear timeout error message ("editor did not appear after N skipOnboarding cycles").

---

### Phase 3 — `mention-search.test.ts` (2 failing tests)

**Goal:** Same root cause as Phase 2 — editor not visible when test acts. Reuse the helper from Phase 2.

**File:** `tests/ts/e2e/mention-search.test.ts:43–47` (`ensureEditorReady`)

**Changes:**
- Replace the body of `ensureEditorReady` with a single `await waitForEditor(page)` call (the helper already handles `skipOnboarding` interleave).
- Remove the now-redundant `editor.waitFor` at line 46.
- Optionally apply `waitForEditor` in `beforeAll` at line 73–74 too.

**Verification:**
```bash
npx vitest run tests/ts/e2e/mention-search.test.ts --config vitest.config.e2e.ts
```

**Exit criteria:**
- `should show mention list when typing @` — passes
- `should filter mention list by search term` — passes
- `should insert mention on Enter and close the list` — already passing; must not regress.

**Dependency:** Phase 2's `waitForEditor` helper. If Phase 2 ships first, this phase is ~5 lines.

---

### Phase 4 — `svg-avatar-upload.test.ts` (1 failing test)

**Goal:** Wait for the SVG→PNG swap to land before asserting on `src`.

**File:** `tests/ts/e2e/svg-avatar-upload.test.ts:189–191`

**Change:**
```ts
await expect.poll(async () => await chatPic.getAttribute('src') ?? '',
    { timeout: 10_000, intervals: [200, 500, 1000] }).toMatch(/\.png/);
```
Drop the preceding `const imgSrc = ...` and the `console.log` (or move log inside the poll for diagnostics).

**Verification:**
```bash
npx vitest run tests/ts/e2e/svg-avatar-upload.test.ts --config vitest.config.e2e.ts
```

**Exit criteria:** `should upload SVG picture in New Chat modal and convert to PNG` — passes.

**Risk:** If conversion legitimately takes >10s in CI, bump the timeout. The `blob:` → `.png` swap is server-driven, so timeout reflects backend speed.

---

### Phase 5 — `TranslationUITest.cs` (out of scope, separate task)

Not addressed in this branch. Track as separate work — file: `tests/Chat.UI.Blazor.IntegrationTests/TranslationUITest.cs:60` and `:388`. Likely either a translation flag/setting change or a renamed UI element. Recommend bisect against last green nightly.

---

## Summary table

| Phase | Files touched | Test failures resolved | Depends on |
|---|---|---|---|
| 0 | helpers.ts | (reduces flakiness in 1–3) | — |
| 1 | avatar-edit.test.ts | 3 | — (benefits from 0) |
| 2 | helpers.ts, signin-and-message.test.ts | 2 | — |
| 3 | mention-search.test.ts | 2 | Phase 2 (helper reuse) |
| 4 | svg-avatar-upload.test.ts | 1 | — |
| 5 | TranslationUITest.cs | 2 (C#) | separate task |
