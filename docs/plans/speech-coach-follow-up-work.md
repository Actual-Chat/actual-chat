# Speech Coach follow-up work

## Scope and order

The weekly comparison correctness work is tracked in
[issue #5082](https://github.com/Actual-Chat/actual-chat/issues/5082) on branch
`fix/coach-weekly-comparisons` and is approved for implementation first.
Step 1 is committed as `98a4feb05f` and pushed in
[draft PR #5085](https://github.com/Actual-Chat/actual-chat/pull/5085): weekly comparisons include all
three summary metrics, apply the configured word floor to both periods and
also to tagged-word coverage for tagger rates, preserve missing rates, treat
equal distance from an acceptable range as neutral, and resolve one language
for both weekly periods. Null/empty language requests follow the remembered
eligible language, then the most-spoken eligible language over 30 days, then
the primary language.

Validation: 91 Coach unit tests and 41 Coach integration tests passed; the
App.Server build passed. The root CI solution-filter build is blocked by an
existing filter entry for `tests/Core.Benchmarks/Core.Benchmarks.csproj` that
is absent from `ActualChat.sln`.

Step 2 was subsequently approved and committed as `24c3b8192c` on the same
branch. The approved progress-indicator refinement below is implemented on
that branch. Step 3 remains deferred.

## 2. Weekly skill summary in Recent

Implement the selected A design documented in
[speech-coach-recent-skill-progress.md](speech-coach-recent-skill-progress.md).
Preserve the existing conversation feed and its actions.

Implementation status: the Recent card uses one explicit comparison language,
three corrected weekly deltas, percentage-point changes, and server-configured
pace bounds. Heading/row buttons open Progress; a selected row is highlighted,
focused, and scrolled into view. Current-only and insufficient-data states do
not invent baselines or indicators. The rolling-window summary is used only
for language-specific pace configuration, never for weekly coverage counts.
New catalog text is present in all 22 languages and the generated Max catalog.

Validation: 36 label/localization unit tests and 6 focused Coach UI integration
tests passed, including reactive language changes, exclusion/restoration,
summary data states, Progress targeting, and existing Skills/occurrence flows.
App.Server and `npm run build:Verify` passed. Chrome checks covered calendar-week
insufficient-data states, language changes, keyboard activation, desktop and
390px mobile layouts, light/dark themes, and long Max translations. Screenshots
are under `tmp/coach-recent-*`; the populated current-week browser comparison
subsequently passed using an audio-backed Russian test entry. Native-device
checks remain outstanding. A broader Coach UI suite attempt exceeded
the 180-second command budget; the focused regression run passed.

- Show Fillers, Pace, and WeakWords using the corrected weekly comparison data.
- Label the calendar-week comparison “This week” / “Compared with last week.”
  Do not describe it as a rolling seven-day comparison.
- Format filler and weak-word changes in percentage points, separately from
  the existing relative-percentage formatting in Progress.
- Show pace relative to the server-configured comfortable range, not as a
  completion bar. Faster is not automatically better.
- Support unavailable current values, insufficient current speech, missing
  baselines, unchanged values, improvement, and worsening.
- Use one explicit effective language for values, baselines, and pace bounds.
- Open Progress from the heading and visibly target the selected metric when
  a skill row is activated. This does not require a full skill-detail page.
- Localize new text and make actions keyboard-accessible.
- Omit coverage counts unless they represent the exact same period and
  language. Do not derive total coverage from the limited Recent feed.

Validation includes reactive updates after language changes and conversation
exclusion/restoration; transcript/replay actions; desktop/mobile layouts;
light/dark themes; keyboard navigation; long translations; and missing-data
states.

### Reuse

Reuse `CoachWeekDelta`, `CoachScoring`, `CoachLabels`, `CoachUI`,
`CoachRecentTab`, `CoachWeekDeltas`, `Card`, and existing Coach styles.

A new `CoachRecentSkillSummary` should remain beside the Coach components:
its content and navigation are feature-specific. Shared Blazor placement is
an option if another consumer needs the indicator; otherwise do not introduce
an abstract progress framework. Extend `CoachUI` for navigation state rather
than adding a new service. No TypeScript work is inherently required.

### Progress indicators

The approved refinement uses progress-oriented line arrows in Recent and
Progress: light-green `↑` for noticeable improvement, dark-green `→` for broadly
stable results, and orange `↓` for noticeable worsening. The numerical values
retain their original direction: a falling filler rate is an improvement.
Missing current or baseline data stays muted and has no arrow.

Filler and weak-word rate changes below 0.5 percentage points are stable. Pace
compares distance from the language-specific comfortable range; distance changes
below 5 words/min are stable, and movement entirely within the range is always
neutral. Exact cutoffs count as noticeable. These are UX tolerances, not
statistical-confidence claims, and do not alter raw values, bands, or scores.
The neutral caption is localized as “about the same” in all supported languages.

Validation: 116 Coach unit tests, 41 label/localization unit tests, and two focused
UI integration tests passed. Tests cover both sides of each cutoff, preserved
raw values, missing data, range-neutral pace, and improvement/stable/worsening
indicators on both surfaces. App.Server and `npm run build:Verify` passed;
mechanical style and diff checks passed. Chrome verified populated Recent and
Progress indicators and the refreshed Russian neutral caption; light/dark
screenshots are under `tmp/coach-trend/`.

## 3. Persistent binary storage benchmark

Benchmark actual .NET serialization before proposing a storage migration.
The earlier indexed-binary estimates are not measurements of the application's
serializers and are not sufficient to justify a schema change.

### Baseline

The localhost audit used `ac_dev_chat` and `ac_dev_users`. It contained one
coached user, 72 analyzed messages, 11 conversation runs, and 8 daily aggregate
rows over 3 dates. Messages averaged approximately 23 words, so this is not a
representative production corpus.

Measured average live row sizes:

| Record | Bytes |
| --- | ---: |
| Chat entry analysis | 554 |
| Users entry contribution | 1,051 |
| Chat conversation analysis | 188 |
| Users run contribution | 655 |
| Daily aggregate | 1,156 |

The four Coach tables contained approximately 131 KiB of live row data and
allocated 464 KiB including indexes and database overhead. These figures
exclude source transcripts/audio, shared operation/event logs, backups, and
WAL. Temporary audit artifacts are under `tmp/speech-coach-review/storage-*`;
recreate the audit if those artifacts are unavailable.

### Experiment

- Serialize representative `SpeechSpan`, `CoachRecord`, and `CoachDay` values
  using the application's actual MessagePack serializers.
- Compare bytes, serialization/deserialization cost, and allocation against
  current JSON/text and JSONB storage.
- Include empty findings, long transcripts, many findings, synonyms,
  multilingual strings, large daily dictionaries, and removed records.
- Account separately for database row/index overhead and encoding savings.
- Evaluate a versioned `bytea` payload while retaining searchable relational
  columns and existing replacement, exclusion, tombstone, and rebuild behavior.
- Evaluate removing identifiers/timestamps/version from the payload where the
  same information is already stored in relational columns.
- Do not remove per-message contributions: edits, exclusions, correction,
  reanalysis, and rebuilds depend on them.

Deliver a benchmark report and migration recommendation, not an automatic
schema migration. A migration requires its own compatibility, rollout, and
validation plan.

### Reuse

Reuse the existing MessagePack-annotated API models and
`ActualChat.Serialization.VersionedByteSerializer` in the Core project;
verify the serializer configuration from source when implementing the benchmark.
Do not create a new general-purpose serialization framework.

Storage-specific Coach contribution schemas belong with the existing Coach
models/services. If a genuinely reusable lexical-ID or compact-encoding
component becomes necessary, compare feature-local placement with
`ActualChat.Core` and prefer Core for dependency-free reusable logic. Such a
component is not required for the initial benchmark.
