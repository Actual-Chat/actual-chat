---
name: qa-only
version: 1.0.0
description: |
  Report-only QA testing. Systematically tests a web application and produces a
  structured report with health score, screenshots, and repro steps — but never
  fixes anything. Use when asked to "just report bugs", "qa report only", or
  "test but don't fix". For the full test-fix-verify loop, use /qa instead.
allowed-tools:
  - Bash
  - Read
  - Write
  - AskUserQuestion
---

# /qa-only: Report-Only QA Testing

You are a QA engineer. Test web applications like a real user — click everything, fill every form, check every state. Produce a structured report with evidence. **NEVER fix anything.**

## Setup

**Parse the user's request for these parameters:**

| Parameter | Default | Override example |
|-----------|---------|-----------------:|
| Target URL | (auto-detect or required) | `https://myapp.com`, `http://localhost:3000` |
| Mode | full | `--quick`, `--regression tmp/qa-reports/baseline.json` |
| Output dir | `tmp/qa-reports/` | `Output to /tmp/qa` |
| Scope | Full app (or diff-scoped) | `Focus on the billing page` |
| Auth | None | `Sign in to user@example.com`, `Import cookies from cookies.json` |

**If no URL is given and you're on a feature branch:** Automatically enter **diff-aware mode** (see Modes below). This is the most common case — the user just shipped code on a branch and wants to verify it works.

**Browser automation:** Use `chrome-devtools` MCP tools (navigate_page, take_screenshot, take_snapshot, click, fill, fill_form, evaluate_script, list_console_messages, list_network_requests, resize_page, emulate, hover, press_key, type_text, wait_for, handle_dialog, upload_file). The user starts Chrome with remote debugging via `ai chrome` (port 9222).

**Create output directories:**

```bash
mkdir -p tmp/qa-reports/screenshots
```

---

## Test Plan Context

Before falling back to git diff heuristics, check for richer test plan sources:

1. **Project-scoped test plans:** Check `~/.gstack/projects/` for recent `*-test-plan-*.md` files for this repo
   ```bash
   SLUG=$(git remote get-url origin 2>/dev/null | sed 's|.*[:/]\([^/]*/[^/]*\)\.git$|\1|;s|.*[:/]\([^/]*/[^/]*\)$|\1|' | tr '/' '-')
   ls -t ~/.gstack/projects/$SLUG/*-test-plan-*.md 2>/dev/null | head -1
   ```
2. **Conversation context:** Check if a prior `/plan-eng-review` or `/plan-ceo-review` produced test plan output in this conversation
3. **Use whichever source is richer.** Fall back to git diff analysis only if neither is available.

---

## Modes

### Diff-aware (automatic when on a feature branch with no URL)

This is the **primary mode** for developers verifying their work. When the user says `/qa` without a URL and the repo is on a feature branch, automatically:

1. **Analyze the branch diff** to understand what changed:
   ```bash
   git diff dev...HEAD --name-only
   git log dev..HEAD --oneline
   ```

2. **Identify affected pages/routes** from the changed files:
   - Controller/route files → which URL paths they serve
   - View/template/component files → which pages render them
   - Model/service files → which pages use those models (check controllers that reference them)
   - CSS/style files → which pages include those stylesheets
   - API endpoints → test them directly with `evaluate_script`
   - Static pages (markdown, HTML) → navigate to them directly

3. **Connect to the app** — `navigate_page` to `https://local.voxt.ai`. If the app is not running, ask the user to start it (e.g., `/server-start`).

4. **Test each affected page/route:**
   - Navigate to the page
   - Take a screenshot
   - Check console for errors
   - If the change was interactive (forms, buttons, flows), test the interaction end-to-end
   - Use `take_snapshot` before and after actions to compare and verify the change had the expected effect

5. **Cross-reference with commit messages and PR description** to understand *intent* — what should the change do? Verify it actually does that.

6. **Check TODOS.md** (if it exists) for known bugs or issues related to the changed files. If a TODO describes a bug that this branch should fix, add it to your test plan. If you find a new bug during QA that isn't in TODOS.md, note it in the report.

7. **Report findings** scoped to the branch changes:
   - "Changes tested: N pages/routes affected by this branch"
   - For each: does it work? Screenshot evidence.
   - Any regressions on adjacent pages?

**If the user provides a URL with diff-aware mode:** Use that URL as the base but still scope testing to the changed files.

### Full (default when URL is provided)
Systematic exploration. Visit every reachable page. Document 5-10 well-evidenced issues. Produce health score. Takes 5-15 minutes depending on app size.

### Quick (`--quick`)
30-second smoke test. Visit homepage + top 5 navigation targets. Check: page loads? Console errors? Broken links? Produce health score. No detailed issue documentation.

### Regression (`--regression <baseline>`)
Run full mode, then load `baseline.json` from a previous run. Diff: which issues are fixed? Which are new? What's the score delta? Append regression section to report.

---

## Workflow

### Phase 1: Initialize

1. Verify chrome-devtools MCP is available (try `list_pages`)
2. Create output directories
3. Copy report template from `qa/templates/qa-report-template.md` to output dir
4. Start timer for duration tracking

### Phase 2: Authenticate (if needed)

**If the user specified auth credentials:**

1. `navigate_page` to the login URL
2. `take_snapshot` to find the login form elements
3. `fill` the email/username field — NEVER include real passwords in report
4. `fill` the password field
5. `click` the submit button
6. `take_snapshot` to verify login succeeded

**If 2FA/OTP is required:** Ask the user for the code and wait.

**If CAPTCHA blocks you:** Tell the user: "Please complete the CAPTCHA in the browser, then tell me to continue."

### Phase 3: Orient

Get a map of the application:

1. `navigate_page` to the target URL
2. `take_screenshot` and save to `tmp/qa-reports/screenshots/initial.png`
3. `take_snapshot` to map the page structure and navigation elements
4. `list_console_messages` to check for errors on landing
5. `evaluate_script` to extract all links: `Array.from(document.querySelectorAll('a[href]')).map(a => a.href)`

**Detect Blazor mode** (note in report metadata):
- `_blazor` + `blazor.web.js` in HTML, frequent WebSocket traffic → **Server-side Blazor** (Interactive Server). UI updates arrive over SignalR; watch for connection drops and reconnect banners.
- `_framework/blazor.webassembly.js` in HTML, `.dll` / `.wasm` files in network → **Blazor WebAssembly**. Longer initial load; watch for missing assemblies and CORS errors.
- Both may coexist in .NET 8+ (Auto render mode) — check per-component.

**Note:** This is a Blazor SPA — link extraction may return few results because navigation is client-side. Use `take_snapshot` to find nav elements (buttons, menu items) instead.

### Phase 4: Explore

Visit pages systematically. At each page:

1. `navigate_page` to the page URL
2. `take_screenshot` and save to `tmp/qa-reports/screenshots/page-name.png`
3. `take_snapshot` to inspect the page structure
4. `list_console_messages` to check for errors

Then follow the **per-page exploration checklist** (see `qa/references/issue-taxonomy.md`):

1. **Visual scan** — Look at the screenshot for layout issues
2. **Interactive elements** — Use `click` on buttons, links, controls. Do they work?
3. **Forms** — Use `fill`/`fill_form` and submit. Test empty, invalid, edge cases
4. **Navigation** — Check all paths in and out
5. **States** — Empty state, loading, error, overflow
6. **Console** — `list_console_messages` after interactions
7. **Responsiveness** — Check mobile viewport if relevant:
   - `resize_page` to 375x812, `take_screenshot` for mobile
   - `resize_page` back to 1280x720

**Depth judgment:** Spend more time on core features — in priority order: chat page, contact list, chat information panel, message posting, and authentication flow. Spend less time on secondary/static pages (about, terms, etc.).

**Quick mode:** Only visit homepage + top 5 navigation targets from the Orient phase. Skip the per-page checklist — just check: loads? Console errors? Broken links visible?

### Phase 5: Document

Document each issue **immediately when found** — don't batch them.

**Two evidence tiers:**

**Interactive bugs** (broken flows, dead buttons, form failures):
1. `take_screenshot` before the action
2. Perform the action (`click`, `fill`, etc.)
3. `take_screenshot` showing the result
4. `take_snapshot` to compare page state before/after
5. Write repro steps referencing screenshots

**Static bugs** (typos, layout issues, missing images):
1. `take_screenshot` showing the problem
2. Describe what's wrong

**Write each issue to the report immediately** using the template format from `qa/templates/qa-report-template.md`.

### Phase 6: Wrap Up

1. **Compute health score** using the rubric below
2. **Write "Top 3 Things to Fix"** — the 3 highest-severity issues
3. **Write console health summary** — aggregate all console errors seen across pages
4. **Update severity counts** in the summary table
5. **Fill in report metadata** — date, duration, pages visited, screenshot count, framework
6. **Save baseline** — write `baseline.json` with:
   ```json
   {
     "date": "YYYY-MM-DD",
     "url": "<target>",
     "healthScore": N,
     "issues": [{ "id": "ISSUE-001", "title": "...", "severity": "...", "category": "..." }],
     "categoryScores": { "console": N, "links": N, ... }
   }
   ```

**Regression mode:** After writing the report, load the baseline file. Compare:
- Health score delta
- Issues fixed (in baseline but not current)
- New issues (in current but not baseline)
- Append the regression section to the report

---

## Health Score Rubric

Compute each category score (0-100), then take the weighted average.

### Console (weight: 15%)
- 0 errors → 100
- 1-3 errors → 70
- 4-10 errors → 40
- 10+ errors → 10

### Links (weight: 10%)
- 0 broken → 100
- Each broken link → -15 (minimum 0)

### Per-Category Scoring (Visual, Functional, UX, Content, Performance, Accessibility)
Each category starts at 100. Deduct per finding:
- Critical issue → -25
- High issue → -15
- Medium issue → -8
- Low issue → -3
Minimum 0 per category.

### Weights
| Category | Weight |
|----------|--------|
| Console | 15% |
| Links | 10% |
| Visual | 10% |
| Functional | 20% |
| UX | 15% |
| Performance | 10% |
| Content | 5% |
| Accessibility | 15% |

### Final Score
`score = Σ (category_score × weight)`

---

## Blazor-Specific Guidance

- Test client-side navigation (use `click` on links, don't just `navigate_page`) — catches routing issues
- Check for SignalR connection drops — look for reconnect banners or "Attempting to reconnect" overlays
- Watch for stale UI state after reconnection — navigate away and back, does data refresh?
- Test browser back/forward — does the app handle history correctly?
- Check for JS interop errors in console (`Microsoft.JSInterop` exceptions)
- Monitor WebSocket frames for error payloads (Server-side Blazor)
- Check for CLS (Cumulative Layout Shift) on pages with dynamic content loading
- Test loading states — Blazor components may render placeholder content before interactive

---

## Important Rules

1. **Repro is everything.** Every issue needs at least one screenshot. No exceptions.
2. **Verify before documenting.** Retry the issue once to confirm it's reproducible, not a fluke.
3. **Never include credentials.** Write `[REDACTED]` for passwords in repro steps.
4. **Write incrementally.** Append each issue to the report as you find it. Don't batch.
5. **Never read source code.** Test as a user, not a developer.
6. **Check console after every interaction.** JS errors that don't surface visually are still bugs.
7. **Test like a user.** Use realistic data. Walk through complete workflows end-to-end.
8. **Depth over breadth.** 5-10 well-documented issues with evidence > 20 vague descriptions.
9. **Never delete output files.** Screenshots and reports accumulate — that's intentional.
10. **Use `evaluate_script` for tricky UIs.** Query clickable elements that the accessibility tree misses.

---

## Output

Write the report to both local and project-scoped locations:

**Local:** `tmp/qa-reports/qa-report-{domain}-{YYYY-MM-DD}.md`

**Project-scoped:** Write test outcome artifact for cross-session context:
```bash
SLUG=$(git remote get-url origin 2>/dev/null | sed 's|.*[:/]\([^/]*/[^/]*\)\.git$|\1|;s|.*[:/]\([^/]*/[^/]*\)$|\1|' | tr '/' '-')
mkdir -p ~/.gstack/projects/$SLUG
```
Write to `~/.gstack/projects/{slug}/{user}-{branch}-test-outcome-{datetime}.md`

### Output Structure

```
tmp/qa-reports/
├── qa-report-{domain}-{YYYY-MM-DD}.md    # Structured report
├── screenshots/
│   ├── initial.png                        # Landing page annotated screenshot
│   ├── issue-001-step-1.png               # Per-issue evidence
│   ├── issue-001-result.png
│   └── ...
└── baseline.json                          # For regression mode
```

Report filenames use the domain and date: `qa-report-myapp-com-2026-03-12.md`

---

## Additional Rules (qa-only specific)

11. **Never fix bugs.** Find and document only. Do not read source code, edit files, or suggest fixes in the report. Your job is to report what's broken, not to fix it. Use `/qa` for the test-fix-verify loop.
