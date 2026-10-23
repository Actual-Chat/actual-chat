# CI watchdog

Watches every red CI run, re-runs the known flakes on `dev`, keeps a record of each failure,
and once a day turns those records into a digest, flake issues and triage cards. This page is
how it fits together and why it is built this way. Changes to any part of it — the workflow,
the script, the agent's prompt, the card format — come with a line in [Design decisions](#design-decisions).

## How it fits together

| Part | Where | What it does |
|---|---|---|
| Workflow | [`.github/workflows/ci-watchdog.yml`](../workflows/ci-watchdog.yml) | Fires on every completed `Build test and deploy`, `Run slow tests` and `Run nightly tests` run that failed |
| Script | [`scripts/CiWatchdog.ps1`](../../scripts/CiWatchdog.ps1), tests in [`tests/ps/unit/CiWatchdog.Tests.ps1`](../../tests/ps/unit/CiWatchdog.Tests.ps1) | Reads the run's jobs and logs, picks a verdict, re-runs, stores the record, writes a journal note |
| Records | branch `ci-watchdog-data`, `records/<year>/<month>/<runId>-<attempt>.json` | One file per red run attempt: verdict, failed jobs and tests, error, stack, condensed output |
| Registry | open issues labelled `ci-flaky`, grouped under #4997 | The known flakes: a test-name pattern in the title, an optional `Symptom:` regex in the body |
| Journal | issue #4640 | A note per notable red run on `dev`, and the daily digest |
| Daily agent | Claude Code routine "CI watchdog (Actual-Chat)", 04:15 UTC; its prompt is [`agent.md`](agent.md) | Reads the records, files new flakes, closes quiet ones, writes triage cards and the digest |
| Card format | [`card.md`](card.md) | What a triage card says and how people grade it |
| Patterns | [`docs/testing/flake-patterns.md`](../../docs/testing/flake-patterns.md) | The mechanisms CI has shown, the five actions, what a test fix may do — for people and the agent alike |

### Per run: the workflow

1. **Verdict.** The failed steps and the test names in the log decide it: `KnownFlake` (every
   failed test matches the registry), `NewFailure`, `RepeatedFlake` (known flakes failing again on
   a re-run of the same commit), `HostCrash` / `HostHang` (a test host was lost), `Collapse`,
   `Unparsed` (the log carries no test names), `Infra`, `Build`, `Deploy`, `Mixed`, `Garbage` (a
   ref moved while it ran).
2. **Re-run.** Only on `dev`, only the first attempt, only while the run still tests the tip of
   `dev`, and only for `KnownFlake` or `Infra`: `gh run rerun --failed`.
3. **Record.** Stored after the re-run, so it says what was done rather than what was meant.
4. **Journal note** on #4640 for a notable verdict on `dev`, a re-run, or a re-run that could not
   start.
5. **Notify Voxt** for red on `dev` or `release/*` that was not re-run.

### Once a day: the agent

The routine's own prompt is a stub that tells it to follow [`agent.md`](agent.md) from its
checkout of `dev`, so a merged change to that file is live from the next run. The agent:

- cuts its window by the records' arrival time on `ci-watchdog-data`, from the `Triaged up to:`
  marker of the last digest, plus any record no digest has listed;
- tells a regression from a new flake, a broken branch, a repeated flake and a new symptom;
- may comment, file a flake issue, open a regression issue, close a quiet flake issue and post
  triage cards — and nothing else: it never changes files, pushes or re-runs;
- posts the digest to #4640, with a `Decisions` list of the cards it wrote.

A person reads the digest, takes or drops each card, and grades the card with a reaction once
its outcome is known ([`card.md`](card.md)).

## Design decisions

Grouped by part, newest first within each. One entry per decision: what, why, and what was
turned down. A decision that changes one of these adds or replaces its entry in the same PR.

### Overall shape

- **2026-10-22 — The agent's prompt and the card format live here, in the repo.** The routine
  keeps only a stub that points at [`agent.md`](agent.md). Why: a prompt kept in the trigger had
  no history, no review and no rollback. Rejected: `docs/`, which is the team's handbook built
  into a site, not a tool's internals; and moving `flake-patterns.md` here as a whole, which
  would cut it out of `waiting.md` and the site's sidebar, where people fixing a test find it.
- **2026-10-21 — The flake-pattern catalogue is a page in `docs/testing/`, not part of the
  prompt.** People, the agent and any future workflow read the same page, and a new mechanism is
  an ordinary PR.
- **2026-10-21 — Automation grows in stages, gated by measured accuracy:** (1) the agent closes
  quiet entries itself, (2) read-only triage cards, (3) draft test-only PRs, (4) acting on replies
  like "ok 1, 3". Only 1 and 2 are built. Stage 3 waits for at least 2/3 of graded cards to be
  right and no `fix-test` that hid a product bug, measured until about 2026-11-04 (#5546). If
  built, it is a separate Actions workflow, since the cloud sandbox has no PostgreSQL, Redis or
  `gh`. Never: auto-merge, or the agent changing product code for a flake — "test or product?"
  is a judgement call, and a wrong one hides a real bug.
- **2026-09-23 — Flakes are fixed by family, never hidden wholesale.** Rejected: re-running
  failed tests inside the job, `[FlakyFact]` everywhere, and watchdog re-runs on every branch —
  each hides intermittent real bugs, and the last races `cancel-in-progress`.
- **2026-09-23 — "CI overload causes the flakes" was tested and ruled out:** flakes appeared even
  with test projects run one at a time (`-m:1`). Bigger runners and more job splitting were
  dropped with it; don't repeat the experiment.
- **2026-09-20 — Deterministic classification runs in GitHub Actions; judgement runs in a daily
  cloud routine.** Actions has `gh` and the token out of the box and reacts at once; telling a
  flake from a regression needs history across runs and branches.
- **2026-09-08 — The watchdog detects; it does not fix.** Rejected after a survey of 248 red
  runs: an agent that fixes flakes and opens PRs (it would have raised 15 timeouts and erased
  the signal that runners are overloaded), and reproducing flakes locally (they don't reproduce
  on an idle machine).

### The workflow and its records

- **2026-10-22 — Records are self-contained:** the first ten failed xUnit tests of a job keep
  their `Stack` and a condensed `Output`. Why: the cloud agent sees only the last 5000 lines of a
  job log, and its network can't reach the storage the full log redirects to; #5565 got an
  `instrument` card while the log already held the cause. Rejected: opening
  `*.blob.core.windows.net` to the agent — too broad for an agent with write rights, invisible
  configuration, logs expire after 90 days, and ~1.5M characters against ~30 KB.
- **2026-10-22 — `HostCrash` and `HostHang` verdicts** rank above `Collapse` and the test
  verdicts, since a lost host takes its unrun tests with it. Never re-run; journaled on `dev`.
  Crashes belong to #5486 and are investigated with dumps from the test jobs, not with more
  watchdog logic.
- **2026-10-22 — Network errors in build steps (ENOTFOUND, ETIMEDOUT, ECONNRESET) stay `Build`,
  not `Infra`, for now,** so they are not re-run.
- **2026-10-21 — A test job's log without failed tests or a totals line is fetched again after 1
  and 2 more minutes before it counts as `Unparsed`:** `workflow_run` fires before the last job's
  log is archived.
- **2026-10-09 — Large records go through `gh api --input`; errors are capped at 500 characters,
  test names are never cut, a journal note lists at most 30 tests per job** — the 128 KB argument
  limit and GitHub's 65,536-character comment limit.
- **2026-10-08 — The parser reads vitest output too,** so E2E tests can enter the registry. The
  `Collapse` threshold of 5 applies to vitest unchanged.
- **2026-10-06 — Transient deploy failures are retried inside each deploy step, not by the
  watchdog,** which acts too late, only at the tip of `dev`, and would have to call a deploy
  `Infra`.
- **2026-10-01 — `RepeatedFlake`:** a known flake failing again on a re-run attempt is journaled
  and never re-run. A flake does not fail twice on one commit.
- **2026-09-24 — The watchdog decides whether a red run reaches Voxt:** `notify-voxt.yml` is
  called from its `notify` job, for `dev` and `release/*`, only when it did not re-run. Rejected:
  a condition inside Notify Voxt, which runs before any verdict exists. Accepted gap: a re-run
  cancelled by a newer push sends nothing; the newer run reports if it fails.
- **2026-09-20 — Re-run only on `dev`, only the first attempt, only while the run still tests the
  tip, only for `KnownFlake` or `Infra`.** The attempt rule stops it re-running its own re-run;
  the tip rule avoids an undocumented interaction with `cancel-in-progress`, and stale red needs
  no re-run anyway. `Deploy` is never re-run: uploading to a store twice is unsafe.
- **2026-09-20 — Feature branches get records only:** no re-runs, notes or messages. Red there
  is normal and its author is watching; re-runs would burn runners at someone else's cost.
  Their records are still kept, because a flake and a regression differ only across branches.
- **2026-09-20 — One record per run attempt, `records/<y>/<m>/<runId>-<attempt>.json` on the
  orphan branch `ci-watchdog-data`:** concurrent runs never compete for a path, and a repeated
  write is a no-op. `Garbage` runs (a ref moved while the run ran) are not recorded at all.
- **2026-09-20 — The verdict comes from each failed job's earliest failed step, per job,** which
  alone separates a moved ref, infrastructure, a broken build and failed tests. A broken build
  next to a known flake is `Mixed`, not `KnownFlake`, or the flake would buy the build a silent
  re-run. `Collapse` is 5 or more failed tests in one assembly: the fixture went down.

### The registry

- **2026-10-21 — The agent closes an entry with no failure in 14 days itself** (a failure with a
  different error still counts), at most three per run, and only after checking that record
  collection is alive — parsed test failures in the last 7 days, at least one on `dev` —
  otherwise a broken parser would empty the registry. Rejected: a higher cap, and a cap of a
  third of the open entries.
- **2026-10-01 — An optional `Symptom:` regex per entry:** the same test failing with another
  error is reported, not re-run. An empty error matches by name; an invalid regex matches
  nothing, so the mistake shows. Timeout-only entries get none, since a timeout text narrows
  nothing.
- **2026-10-01 — Every open entry is a sub-issue of #4997, in priority order.** Other CI issues
  link it as "Related", because a sub-issue of #4997 is a registry entry by definition.
- **2026-09-29 — The label `ci` groups CI problems for people and never triggers a re-run;**
  only `ci-flaky` does.
- **2026-09-21 — Patterns name the method, not the class,** unless three methods of the class
  failed: a class-wide pattern would silently re-run `dev` over a different regression in the
  same class.
- **2026-09-20 — The registry is the open `ci-flaky` issues, not a list in the script.** The
  in-script list was stale before the script was finished (4 of its 15 entries already fixed),
  and a stale entry silently re-runs `dev` over a regression. Closing the issue — from the fixing
  PR, `Closes #N` — takes the test off. An unreadable registry means no known flakes: an extra
  report costs a minute, a wrong silent re-run costs a regression.

### The daily agent

- **2026-10-22 — A returned flake is checked against the fix that closed it, gets a priority 2
  card, and its fix's author decides whether to reopen.** Why: the agent only commented on the
  closed issue, so the test stayed out of the registry and nobody owned the decision; #4862
  returned on 2026-10-20 and was reopened by hand two days later. A failure on a commit without
  the fix is only an old branch, not a return. Rejected: letting the agent reopen, because a
  bare timeout doesn't show the old mechanism is back, and a person picks between reopening and
  a new issue.
- **2026-10-23 — Priority 5 orders by the number of eligible failures, then by the oldest one.**
  Why: oldest-first alone carded #5427 (2 failures) and #5310 (1) while #5417, the most frequent
  entry with 4, waited a second day. The 2026-10-22 order came along with widening the window and
  was never weighed against the count: the 14-day window is what keeps a missed entry from being
  dropped, not the order. Starvation doesn't come back: a carded entry leaves priority 5.
- **2026-10-22 — The card cap stays at three; priority 5 takes any entry without a card that
  failed in the last 14 days, counting only failures on a commit that contains the test file's
  latest change,** oldest first (the order was replaced on 2026-10-23). Why: entries that missed the cap were dropped the next day
  unless they failed again, and a failure from before the test last changed shows nothing new
  (#5523 for #5497). Rejected: raising the cap to 5, and excluding entries under "Open
  mechanisms".
- **2026-10-22 — "Evidence out of reach is not evidence missing":** when the record lacks the
  output and the fetched lines don't hold it, the action is `none` with "needs the full job
  log", not `instrument`.
- **2026-10-21 — The routine runs on Opus 5.5.** In the dry run it caught a returned flake
  (#4862) that Sonnet missed, and finished faster.
- **2026-10-21 — A push notification only when `Urgent` lists something.** Rejected: never, and
  after every run.
- **2026-10-21 — A repeating nightly E2E failure is filed as a task with `ci`, not `ci-flaky`:**
  the label would silently re-run the 1.5 h nightly.
- **2026-09-22 — The window is cut by a record's arrival on the data branch, not the run's
  `CreatedAt`,** since records land 15 minutes to over 2 hours after the run (run 35524455582
  was missed). Every digest lists its `Triaged runs:`, and a record no digest has listed is
  pulled into the next window however old.
- **2026-09-22 — Hold a filing when a commit that looks like the fix landed after the last
  failure:** filing an already-fixed test makes the registry mute its next real failure.
- **2026-09-20 — Filing threshold: two failures on two branches within 14 days;** a nightly-only
  test counts nights instead of branches. A regression gets `ci-regression` and a mention.
- **2026-09-20 — The routine can't change files:** `disallowed_tools` removes Write, Edit and
  NotebookEdit. A whitelist would also have cut off `mcp__github__*`. With Bash allowed, the ban
  on writes still rests on the prompt.

### Triage cards

- **2026-10-22 — People grade cards with reactions:** 👍 right, 😕 mechanism right but the fix
  different, 👎 wrong. The agent doesn't read them; they are collected to measure accuracy
  (#5546), and docs PRs say `Refs #5546` so the measurement isn't closed early. Replies like
  "ok 1, 3" are notes for colleagues; nothing acts on them.
- **2026-10-22 — Before `instrument`, map each possible answer to an action;** if they all lead
  to the same one, take it now. Check each candidate mechanism against the code first (#5564).
- **2026-10-21 — A test fix may only make the check stricter or equivalent;** never weaken an
  assertion, raise a budget, or add `FlakyFact`, `Skip`, retries or product changes. A red test
  turned green by asking less of the product becomes a blind spot.
- **2026-10-21 — One action per card, a tests-only draft, `high` confidence only with proof, no
  `fix-test` at `low`; one card per signature,** except a new failure on the commit that carries
  the change an earlier `wait` or `instrument` card asked for.

### Around the watchdog

- **2026-10-22 — `[FlakyFact]` and `[FlakyTheory]` are gone, and so is `FlakyTest.XUnit`.** Under
  Xunit.DependencyInjection they never retried: it runs a test case by its base type,
  `XunitTestCase`, and never calls the retrying `FlakyTestCase.RunAsync`. A runner wrapper that makes
  them retry was written and kept off `dev`: retries would hide flakes from the watchdog, which sees
  red runs only, and the registry already says which tests are flaky. Rejected: keeping the
  attributes as markers, since merging the wrapper later would silently turn all 16 into retries.
- **2026-10-22 — Test and flake fixes are not cherry-picked to release branches:** a release
  branch is rebuilt only for hotfixes and not re-run after, so a flake there costs at most one
  extra re-run. Product fixes are decided case by case.
- **2026-10-14 — A PR may merge with red CI when the failures are known flakes, failures shared
  with `dev`, or environment problems; CI is never re-run without an explicit request.**
- **2026-10-01 — A PR that doesn't fix a flake says `Refs #N`, with no fix or close word next to
  the number:** "does not fix #4858" closed #4858 and took it off the registry.
- **2026-09-22 — Wait budgets scale ×3 on a build agent through one helper, `TestWait`;** only
  budgets, never configuration or quality thresholds. Rejected: `.CiScaled()` at every call site.
  See [`docs/testing/waiting.md`](../../docs/testing/waiting.md).
