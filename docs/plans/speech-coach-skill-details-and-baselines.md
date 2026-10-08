# Speech Coach: skill details, speaking-day history, and personal baselines

Status: Measurement capture/persistence and Fillers, Weak Words, and Pace detail screens implemented
on `feat/coach-skill-details`, including daily language histograms and exact segment-based target percentages.
Capture now defaults on at the developer's request; no historical backfill or remote deployment.
Speaking-day history and UTC calendar navigation are implemented. Personal baselines remain open.
Tracking: [#5128](https://github.com/Actual-Chat/actual-chat/issues/5128).
The fine-timing rollout gate remains open; see the [spike evidence](./speech-coach-measurement-storage-spike.md).

## Purpose

Make Coach results understandable and actionable: what was measured, how it changed, what a comfortable
range means, and which moments are worth listening to. Support occasional speakers without portraying
inactive days as zero performance. Let users choose their own historical starting point for comparison.

This follows the shipped weekly-comparison and Recent-summary work. It does not reopen that work or
replace the existing Recent / Progress / Skills structure.

## Design references

Figma file: [Actual chat Desktop](https://www.figma.com/design/EJxXOzem02zhRvuqsJIool/Actual-chat-Desktop).

- [Overview, vocabulary, fillers, and pace](https://www.figma.com/design/EJxXOzem02zhRvuqsJIool/Actual-chat-Desktop?node-id=29239-852599).
- [Filler detail](https://www.figma.com/design/EJxXOzem02zhRvuqsJIool/Actual-chat-Desktop?node-id=29239-852674).
- [Pace detail](https://www.figma.com/design/EJxXOzem02zhRvuqsJIool/Actual-chat-Desktop?node-id=29247-857675).
- [Pace target distribution](https://www.figma.com/design/EJxXOzem02zhRvuqsJIool/Actual-chat-Desktop?node-id=29239-852866).

Use the information hierarchy and target presentation, not unsupported claims from the mockups.
The existing accessible comparison colors and progress-oriented arrows remain authoritative.

## Scope

### Included

- One skill-detail navigation pattern, initially rich for Fillers, WeakWords, and Pace.
- Measurement definitions, coverage, target presentation, contextual examples, and history.
- Calendar period selection and navigation with inactive days compressed out of charts.
- Personal baselines selected from historical or current eligible measurements.
- Fine-grained pace segments from persisted Soniox-derived transcript timing.
- Duration-weighted below / within / above-range distributions.
- Playback of qualifying pace sections when source audio remains accessible.
- Reactive updates after analysis, exclusions, edits, removals, and baseline changes.

### Not included

- Changing the existing score formula, comfortable ranges, or weekly-comparison meaning.
- Population benchmarks, friend comparisons, or leaderboards.
- Vocabulary acquisition/reuse tracking; the current vocabulary metric does not establish this.
- Per-occurrence correction such as “This use is intentional.”
- Editable personal pace targets. The first release uses server-configured language ranges.
- New transcription providers, additional transcription calls per measurement, or new LLM analysis.
- Realtime segment coaching while speech is still being transcribed.
- Automatic wholesale migration of existing Coach JSON/JSONB data or removal of per-entry contributions.
  Binary encoding for new pace data and a measured storage/migration recommendation are in scope.

## Agreed storage and distribution decisions

- Persist target-independent segment measurements, not only below/within/above totals.
- Build absolute, duration-weighted pace histograms separately per language as rebuildable rollups.
- Derive below/within/above for the selected range; targets and baseline selection do not mutate raw data.
- Do not assume a Gaussian distribution or persist deviations from the current mean/target midpoint.
- Add no database row per segment and no new per-message business row for segment storage.
- Include actual binary encoding and persistence benchmarks in the initial measurement/storage spike.
- Any conversion of existing JSON/JSONB data is a separate, explicit rollout decision based on evidence.

## Product invariants

1. Only the caller's eligible, non-excluded speech contributes.
2. A missing measurement is not zero. A real zero filler rate remains valid.
3. Lack of speech is not failure and does not change a saved baseline.
4. Values, comparisons, coverage, graphs, and explanations use one explicit language and scope.
5. Display percentages use declared denominators. Period totals are not averages of daily percentages.
6. Each duration contributes at most once to a pace distribution.
7. A personal baseline, a previous-period reference, and a comfortable range are distinct concepts.
8. Choosing a baseline changes the reference for comparison, not the score or target.
9. Segment pace is an estimate from transcript timing, not an exact acoustic measurement.
10. No new precision claims are enabled until persisted timing has been validated on real recordings.

## 1. Skill-detail experience

### Entry points and navigation

- Activate a skill from Skills, a metric row in Progress, or a skill in the Recent summary.
- Replace the Coach panel content with its detail view; do not introduce another top-level tab.
- Preserve language, selected period, and originating view. Back restores the origin and keyboard focus.
- Keep the existing Recent heading action for opening the overall Progress view.
- Word chips continue into the existing occurrence experience, with Back returning to the skill detail.
- Use the same navigation state on desktop and native/mobile; adapt layout to available panel width.

### Common sections

1. Skill name, language, scope, period, and Back.
2. Current value, unit, comparison reference, and data coverage.
3. History with dated points and an applicable target band/threshold.
4. Comfortable range or threshold, current position, and interpretation.
5. Metric-specific examples and access to playback/context.
6. What is measured, calculation, and limitations.

For metrics without a rich implementation, show the common value and explanation sections without
inventing examples, targets, or confidence. Do not imply every metric has a meaningful target band.

### Fillers and weak words

- Show occurrences per 100 tagged words, equivalent to the existing displayed percentage.
- Explain that fillers include filler words and filled pauses; tagging is an estimate.
- Show measured coverage, common words, and their frequency on the same denominator.
- Reuse marked-word contexts and playback. No correction/feedback mutation in this release.
- Reuse meaningful-change tolerances: below 0.5 percentage points is broadly stable.

### Pace

- Present the language's configured comfortable range; examples are English 130–170 and Russian 100–140 wpm.
- Explain pause handling and timing coverage, not the mockup's unqualified “including pauses.”
- Present the segment distribution and a qualifying section to review when supported.
- Do not claim a sentence-ending acceleration from a fast segment alone.
- Preserve range-oriented improvement semantics; a numerically lower pace is not necessarily better.

## 2. Periods and history for occasional speakers

### Calendar semantics

- Day means a UTC calendar day; Week means Monday–Sunday UTC; Month means a UTC calendar month.
- Show the date range explicitly. Previous/next moves by the selected calendar unit.
- Current periods end at the captured request time; historical periods are closed intervals expressed
  internally as half-open ranges. Future periods cannot be selected.
- Preserve existing rolling-window surfaces outside the new detail view; do not silently relabel them.

### Chart semantics

- Chart points represent eligible speaking days, not every calendar day.
- No entry/column/point is allocated for inactive or metric-ineligible days.
- Equally spaced points use an activity/category axis labelled “Speaking days,” with actual dates.
- Indicate substantial calendar breaks with a date-gap marker, not a sequence of empty columns.
- A connecting line shows progression between observations, not measured behavior during the gap.
- One eligible day produces one point and no invented trend; none produces a useful empty state.
- Offer a longer period when coverage is sparse; never silently fetch older speech into “this week.”
- Keep chart selection and baseline selection usable with keyboard and screen readers.

Aggregate all eligible contributions within the selected period. For pace, use total words divided by
appropriate total speech time; for tagger rates, use total occurrences divided by total tagged words.
Do not take an unweighted mean of plotted values. Eligibility follows the server's configured word and
coverage rules; sparse days may be omitted while the period as a whole remains eligible.

## 3. Personal baselines

### User flow

UI term: “Personal baseline,” rather than an unspecified benchmark.

- “Use current measurements” snapshots the eligible finalized measurements currently displayed.
- “Choose from history” selects a historical calendar period and previews its value and coverage.
- Show dates, speaking days, measured words/time, language, and metric before confirming.
- Selecting one chart point is allowed only when that day's measurements meet eligibility requirements.
- A period may qualify for one metric but not another. Do not silently set baselines for other skills.
- Allow explicit replacement and clearing; never automatically move a saved reference.

### Snapshot semantics

One active baseline per user, normalized language, metric, and supported scope. Initial scope is all
eligible conversations, matching the existing language-filtered skill surfaces.

A snapshot records: baseline identity, selected source period, capture time, cutoff, raw metric value,
weighted numerator/denominator, coverage, measurement definition/version, and provenance sufficient to
validate its source contributions. Do not store only a formatted string or rounded value.

The reference value is immutable. A later analysis delivery must not silently rewrite it. Baseline
creation reads a consistent server snapshot and includes only finalized, eligible records.

### Comparison semantics

- Future progress refers to speech recorded after the baseline's capture cutoff, not the same speech
  that created it. Historical baseline dates and capture time are both retained.
- In baseline mode, intersect the requested measurement period with the post-cutoff interval and
  explicitly label any clipped period as “Since baseline …”. Do not show a full-period value beside a
  delta calculated from a different subset.
- Exclude entries straddling the cutoff from baseline-progress measurements in the first release;
  disclose omitted coverage rather than assigning their complete word counts to either side.
- No eligible post-cutoff measurements means no comparison, not “unchanged.”
- Rates show percentage-point differences and existing meaningful-change classification; zero baselines
  remain valid and must not trigger division by zero.
- Pace improvement compares distance to the current comfortable range for both values. A saved average
  is not itself the ideal target. Show a range-change notice if configuration changed since capture.
- Baseline comparison is primary inside the detail view when selected; previous-period comparison is
  secondary. Existing Recent/Progress weekly comparisons retain their current meaning.
- The history chart labels the baseline line with its period; the target uses a separate shaded band.

### Lifecycle and compatibility

- Source edits, removals, exclusions, or relevant reanalysis invalidate an affected baseline; mark it
  unavailable and ask the user to choose a replacement. Do not silently recompute it.
- Adding unrelated/new speech does not invalidate a snapshot.
- If measurement versions become incompatible, show that comparison is unavailable rather than compare
  unlike definitions. Version backfill alone must not silently alter a saved reference.
- Clearing Coach data removes baseline values and provenance as well as existing coaching rows.
- Losing access to source material must not expose stale contexts or playable references.
- Store small snapshots through existing user-scoped persistence; bound source provenance separately
  if needed. Do not put an unbounded list of source IDs into UserCoachSettings.

## 4. Fine-grained pace measurements

### Source and timing quality

Soniox is used in dev and production. SonioxTranscriptBuilder maps token StartMs/EndMs into Transcript.TimeMap.
Existing audio entries retain a character-to-time LinearMap, and PlayableTextMarkup maps words to it.

Validate the persisted map, not just the provider response. Structural validity is necessary but does not
prove fine resolution: SonioxTranscriptBuilder can fall back to a whole-transcript linear map, and older
or edited entries may have missing/coarse/remapped timing. A detailed-quality decision needs provenance
and/or map-resolution evidence; a non-degenerate map alone is insufficient.

Use finalized stored transcripts. Do not classify an unstable streaming tail as historical measurement.
Respect SpeechTextStats.IsWordSplittable; unsupported word segmentation remains unavailable.

Approved timing simplification: improve the original `TimeMap` for new Soniox recordings, keep historical
maps unchanged, and use existing media information for audio bounds. The developer removed the proposed
timing metadata record, codec, column, and migration, without adding a replacement quality marker.
Do not persist a duplicate timing map. New accurate inputs may change headline pause/speech metrics
and scores; formulas stay unchanged. Final precise maps must survive transcript transport without the
legacy comparison tolerance dropping sub-100-ms corrections.

### Language attribution

- Normalize language keys to ISO codes and keep source measurements and daily histograms separate.
- The existing CoachEntryRecord supplies one language per entry. This supports English and Russian
  recordings in the same history, but does not identify code switching inside an individual recording.
- Initial attribution is entry-level and must be labelled as such. Do not infer token-language boundaries
  from Transcript.Languages: it is a language set, not a positional mapping.
- Mixed-language utterances do not block measurement: use their existing entry language for this iteration.
  Do not add positional token-language tracking. Fine within-entry language attribution is deferred.
- An all-language target summary, if exposed, classifies each language against its own range and sums
  durations. It does not pool absolute pace values and apply one language's target to them.

### Segmentation proposal

The following are initial prototype parameters, not validated accuracy guarantees:

- Target segment length: approximately 10 seconds of timed speech context.
- Minimum classified segment: 5 countable words and 3 seconds of analysable duration.
- Split speech blocks at gaps at least ChatSettings.Coach.MinPauseSeconds, currently 1 second.
- Never merge across entries, speakers, entry-language groups, invalid timing spans, or a qualifying long pause.

Algorithm requirements:

1. Derive countable words using the same lexical rules as SpeechTextStats.
2. Map word boundaries using the whitespace-trimming convention established by SpeechTimingStats.
3. Validate finite offsets within audio bounds, positive durations, and monotonic timing. Account for
   subword tokens, repeated boundary points, and interpolation. Do not turn unknown timing into zero.
4. Build speech blocks; include short within-block pauses, exclude long pauses and unmapped intervals.
5. Partition blocks near the target length at word boundaries without overlapping segments or counting
   a word twice. Assign each accepted interval to exactly one segment.
6. Merge an undersized tail with the adjacent segment in its block only within a bounded maximum length
   (initially twice the target length); otherwise retain it as unclassified coverage.
7. Retain relative audio start/end, text offsets, word count, analysed seconds, quality, and version.
8. Calculate segment pace from unrounded words and analysed seconds; round only for display.

Do not average instantaneous per-word rates: those amplify timestamp noise. Do not clamp extreme pace
into the target range; reject invalid measurements with an explicit reason instead.

### Definition boundary with existing pace

Existing headline pace is words / SpeechSeconds, with recording-duration fallback when timing is absent.
SpeechTimingStats subtracts long internal pauses but may retain leading/trailing recording time.
Segment analysis cannot confidently classify unmapped leading/trailing silence.

The prototype must quantify this difference. Existing score/headline formulas remain unchanged in this
feature. The detail must label segment-derived pace and its coverage if it differs from headline pace;
it must not claim identical denominators. Aligning all pace definitions would require a separate explicit
product decision and regression review before shipping, not a hidden change in the segment calculator.

### Target-independent histogram

Retain word counts and analysed durations as the authoritative source. Store integer time units at a
precision validated against persisted timing; do not store an additional rounded pace as the authority.
Audio/text offsets, quality, version, and language attribution support context and reconstruction.

Build a duration-weighted histogram on an absolute wpm axis, independent of the selected range or
baseline. Display the comfortable range as an overlay. The histogram may be asymmetric or multimodal;
do not fit a normal/Gaussian curve as its stored representation or center stored bins on a mutable mean.

Daily logical partitions are user, UTC date, normalized language, and measurement definition/version.
A rollup is derived and rebuildable from accepted per-entry segments. Incompatible measurement versions
cannot be merged; use explicit payload version partitions or rebuild the affected rollup, not accidental
mixing. This does not require a separate relational row for every segment or every version.

Choose fixed bin resolution, sparse/dense encoding, and tail handling after measuring accuracy and size.
Conserve duration across bins. Do not silently clamp outliers into an edge bin or drop histogram tails.
Visual display bins may be coarser than storage bins; combining adjacent bins preserves duration.

A binned histogram cannot give exact totals for arbitrary boundaries falling inside a bin. Derive exact
below/within/above totals from the retained segments or an exact cached aggregation keyed by range and
measurement definition. Do not linearly split a bin without evidence about its internal distribution.
Changing a range invalidates those derived totals, not the segments, and requires no retranscription.

### Distribution semantics

For target [low, high], classify raw segment pace:

- Below: pace < low.
- Within: low <= pace <= high.
- Above: pace > high.

Sum each segment's analysed seconds into one bucket. ClassifiedSeconds = BelowSeconds + WithinSeconds
+ AboveSeconds. BucketPercent = BucketSeconds / ClassifiedSeconds * 100.

Show a three-segment horizontal duration distribution, bucket percentages/minutes, and the actual
thresholds. Below and above are opportunities to review, not diagnoses. Match Figma hierarchy without
using color alone. Display rounding must not make the visible percentage sum misleading.

Show classified coverage and unclassified/unavailable speech separately. Unavailable timing is never
included in the green bucket or quietly replaced by whole-recording classification. If classified
coverage is insufficient, show an unavailable result instead of a confident percentage.

Within-range share is based on short segment averages, not proof of continuous instantaneous pace.
The explanation must say this even when the display title is “Within your comfortable range.”

### Reviewable sections

- Select sustained eligible below/above-range segments, not isolated timestamp spikes.
- Return the entry reference and validated start/end offsets; recheck source access on playback.
- Use existing playback/navigation foundations, extending range playback only where necessary.
- Label the observation factually, such as “A faster section.” Do not add an LLM inference requirement.
- Provide transcript navigation when audio is unavailable. Never promise exact replay boundaries beyond
  the persisted timing's accuracy.

## 5. Data flow, persistence, and reactive behavior

Finalized audio + transcript/time map -> Chat-side deterministic segment analysis -> versioned entry
analysis -> existing CoachEntryAnalyzedEvent -> Users contribution log -> detail/history aggregation -> UI.

- Compute segments on the Chat side where timing and transcript are already available.
- Carry compact segment measurements through existing entry analysis/contribution records; do not copy
  a second full transcript or time map into Users or inflate JSON with per-segment property names.
- Retain counts/durations, not only below/within/above classifications. Current range changes should
  reclassify compatible measurements without retranscription.
- Store target-independent daily histograms and coverage for fast history queries, while retaining
  per-entry measurements for exact target classification, edits, reconstruction, and bounded drill-down.
- Reuse source IDs and version ordering; retries must not duplicate duration contributions.
- Timing-only changes must trigger analysis even when ContentHash is unchanged. Include timing and
  algorithm version in the measurement identity/invalidation decision.
- Replacements remove old contributions before adding new ones; deletions and exclusions remove their
  duration from all relevant aggregates. Restoration is reversible.
- Fine-grained historical backfill uses existing persisted timing when adequate, is bounded/resumable,
  and does not issue transcription requests automatically.
- Old serialized records lacking segment fields mean unavailable/not-yet-analysed, never an empty
  distribution implying zero speech. Distinguish pending backfill from insufficient input.
- Scope every compute/API request to the caller, explicit language, period, and measurement definition.
- Return bounded chart data and example lists. Avoid all-history record scans, per-point RPC calls,
  per-view transcript parsing, and per-render recomputation.
- API/serialized contracts and storage evolution require design before implementation. If migrations
  are required, generate them with the migration tool; this spec does not promise a migration-free path.

### Persistence ownership and row budget

| Store | Existing role | New data and proposed representation |
|---|---|---|
| Chat DbCoachEntry | One authoritative analysis row per analysed voice entry | Versioned binary segment payload in a bytea column on the existing row. |
| Users DbCoachEvent | One source contribution, with versions, exclusions, and tombstones | Versioned binary contribution data containing the measurements; avoid duplicating the same segments inside JSONB. |
| Users DbCoachDay | Shared daily language aggregates | Versioned binary, rebuildable histogram/coverage attached to the existing day/language row. |
| User-scoped baseline storage | User-selected references | Small snapshot per saved baseline, with bounded provenance; not one snapshot per message. |

For an analysed voice message, Coach already normally has one Chat analysis row and one Users contribution
row. This design adds zero new per-message relational business rows; it extends those rows' payloads.
Daily rows are shared by messages for the same user/date/language. Existing conversation-analysis rows
are separate and amortized over conversations. Baseline rows/keys are created when the user saves a
baseline, not when each message arrives. No table with one row per segment or histogram bin is proposed.

There are two intentional persisted segment copies: Chat owns the source analysis, while Users owns the
versioned contribution needed for user-scoped aggregation and exclusions. Count both in the cost model.
Any attempt to eliminate one copy is a separate architecture change, not an assumed serialization win.

A row budget is not a complete storage budget. Include heap/index/TOAST size, PostgreSQL row versions,
WAL, retained operation/outbox events, replicas, and backups separately. Edits/rebuilds can create database
write amplification even when logical row counts stay constant.

### Binary storage and benchmark gate

Current DbCoachEntry.Spans is JSON text; DbCoachEvent.Payload and DbCoachDay.Data are JSONB. API
MessagePack annotations do not change that persistence. New segment/histogram encoding should be designed
for versioned binary storage now rather than first writing large arrays to JSON and deferring the cost.

Reuse VersionedByteSerializer and the existing model serializers where suitable. Benchmark:

- Current JSON/text and JSONB as the baseline.
- Actual application MessagePack serialization with a versioned envelope.
- Compact packed integer/array encoding if MessagePack overhead warrants it.
- Optional compression only where measured size reduction outweighs CPU/allocation cost.
- Sparse vs dense histogram storage and realistic precision/tail alternatives.

Use real and synthetic short/long, sparse/dense, multilingual, and frequently edited inputs. Measure both
new-data incremental cost and the complete existing-plus-new payload. Report .NET byte size, encode/decode
latency, allocations, and PostgreSQL column/heap/index/TOAST size plus write amplification. Include Chat
and Users copies, daily rollups, and event/log serialization; converting database columns does not
implicitly convert operation logs or remove their JSON/base64 overhead.

Planning estimate only: approximately six 10-second segments per minute at 16–32 packed bytes each would
produce about 200–400 bytes across two segment copies per minute, before envelopes and database overhead.
This is not a measured MessagePack result or a whole-database estimate. Replace it with benchmark data
before committing to a storage budget. Preserve the earlier audit in
[speech-coach-follow-up-work.md](./speech-coach-follow-up-work.md) as the historical baseline, not proof of
future savings.

The benchmark selects field precision and encoding before the production contract is finalized. Keep
serialization format version separate from measurement algorithm version. Readers must distinguish
legacy/no-data, pending analysis, valid empty data, unsupported versions, and corrupt payloads. Binary
format fallback must not turn corruption into plausible zero measurements.

### Migration and rollout

Adding new bytea fields and converting existing JSON/JSONB payloads are distinct changes. Decide the
scope of existing-data conversion from measured benefit; do not migrate every Coach table automatically.
The contribution encoding may require a binary payload column plus legacy JSONB reads during rollout.
Do not assume VersionedByteSerializer alone converts an existing JSONB column or solves rolling-reader
compatibility.

Before implementation, specify legacy reads, mixed-version deployments, any temporary dual writes,
bounded/resumable backfill, rollback, and the point at which legacy writes/columns can be retired. Measure
transitional storage overhead as well as the final layout. Generate every required EF migration using
the migration tool, verify no pending model changes, and run migration parity tests. Retain exclusions,
tombstones, source version ordering, and rebuild behavior throughout the transition.

## 6. Reuse

### Existing abstractions to reuse

- SpeechTextStats / SpeechTimingStats / SpeechMetrics: counts, lexical policy, pauses, existing pace.
- Transcript / LinearMap / PlayableTextMarkup / SonioxTranscriptBuilder: timed text, no provider replacement.
- CoachEntryAnalysis / CoachEntryRecord / CoachRecord / CoachEntryAnalyzedEvent: versioned contribution flow.
- CoachDayBuilder.Merge / CoachScoring / CoachScoring.PaceRange / CoachProgressBuilder: weighted summaries,
  configured ranges, eligibility, and meaningful-change semantics. Do not copy formulas into the browser.
- ICoach / ICoachBackend / CoachUI: caller-scoped reactive APIs and navigation.
- CoachLabels / CoachSkillRow / CoachOccurrences / CoachMarkHint: labels, context, explanations, and hints.
- CoachUI.JumpTo and existing audio/time-map playback: navigation/replay foundations, not a new player.
- UserCoachSettings / ServerKvasBackend user scope: preference and small-snapshot persistence conventions.
- DbCoachEntry / DbCoachEvent / DbCoachDay: existing persistence boundaries and row ownership.
- ActualChat.Serialization.VersionedByteSerializer and existing MessagePack serializers: versioned binary
  envelopes and benchmark candidates; no new generic serializer framework.
- Shared Cards and existing chart components. No fitting historical line chart or fine-segment pace
  calculator was found in the inspected .NET/TypeScript catalogs.

### New components and placement

| Component | Local vs shared option | Recommendation |
|---|---|---|
| Skill-detail container and metric sections | Coach UI vs shared generic detail framework | Coach UI; behavior is domain-specific. |
| Timed-word validation and segmentation | Api/Chat/Coach vs primitive-input helper in ActualChat.Core | Prefer Core for substantial dependency-free timing math; keep PlayableTextMarkup adaptation with shared speech analysis in Api. Do not extract a trivial pass-through. |
| Segment/stat contracts | UI-local model vs shared Api/Chat/Coach and Api/Users/Coach | Shared domain contracts used by analysis, events, aggregation, and UI. |
| Detail/history and baseline aggregation | New generic service vs existing Coach/CoachBackend methods | Existing Coach services; no duplicate persistence/scoring layer. |
| Historical line chart with target/reference bands | Coach-only SVG vs UI.Blazor/Components/Charts | Shared UI chart, with no Coach dependencies. Core/Core.Server cannot host a UI component. |
| Three-segment distribution presentation | Coach CSS vs shared distribution component | Start Coach-local; share only if a real reusable chart abstraction is justified. |
| Baseline snapshots and commands | Generic benchmarking engine vs Coach contracts/user-scoped storage | Coach-specific; no unrelated benchmark subsystem. |

A new server-only reusable helper belongs in ActualChat.Core.Server only if it has substance and consumers
beyond Coach. Pure helpers use ActualChat.Core. New TypeScript helpers, if necessary, must be evaluated
for placement under src/nodejs/src rather than hidden inside one Coach component.

## 7. Validation and acceptance criteria

### Measurement tests

- Constant-rate timed speech produces expected pace and exact additive classified duration.
- One recording with slow/comfortable/fast sections does not collapse to a single average category.
- Exact range endpoints are within range; duration weighting differs correctly from message counting.
- No overlaps or double-counted words; accepted plus unclassified intervals reconcile under the declared
  duration definition, including split boundaries, tails, pauses, and invalid spans.
- Subword tokens, punctuation, repeated boundaries, sparse/degenerate maps, leading/trailing silence,
  and very short phrases have deterministic outcomes.
- Compare persisted maps with playback on real Soniox recordings in English and Russian. Include
  natural pauses, pace changes, offline corrections, and transcript edits.
- Define acceptable timing/pace error and classified-coverage gates from these recordings before rollout;
  token timestamps alone are not a validation result.

### Backend/baseline tests

- Language isolation, authentication/ownership, eligibility, tagged-word denominators, valid zero rates.
- Sparse speaking-day periods aggregate correctly; UTC day/week/month and request cutoffs are consistent.
- Baseline capture is stable under new speech and late deliveries; concurrent source mutation cannot
  create a snapshot with mixed revisions.
- No source reuse across the baseline cutoff; empty post-cutoff coverage yields no comparison.
- Replacement/clear, source edit/delete/exclude/restore, measurement version change, and range change.
- Event retries, stale versions, timing-only updates, backfill, and clearing all Coach data.
- Rebuilding aggregates produces the same result as incrementally updating them.
- Changing the target reclassifies stored segments without new transcription or measurement mutation.
- Histogram and exact range totals conserve duration; boundaries inside bins never use guessed fractions.
- Different languages use separate rollups/ranges; combined target shares are duration-weighted.
- Binary round trips, legacy/mixed-version reads, unknown/corrupt payloads, tombstones, and backfill.
- No row-per-segment/bin growth; measured full/incremental storage meets the agreed budget.

### UI acceptance

- All entry points reach the same detail view and Back restores origin/focus/period/language.
- Charts omit inactive days, show actual dates and breaks, and do not imply equal elapsed time.
- Empty, one-point, insufficient-coverage, pending-backfill, and invalid-baseline states are distinct.
- Baseline and target are visually/textually distinct; no contradictory comparison subsets.
- Green/orange meanings and arrows remain consistent with shipped comparison behavior.
- Normal text has at least 4.5:1 contrast in light/dark/ash; graphs have accessible labels and alternatives.
- Desktop, actual 390px viewport, and native devices support selection, navigation, and playback.
- Localize all visible strings in all supported UI languages; regenerate BCMS and Max catalogs.
- Use TestWait for test convergence, including WhenRendered, and align test ceilings with CI-scaled waits.
- Follow the required .NET and TypeScript validation commands when those files are implemented.

## 8. Delivery slices and review gates

1. Measurement/storage spike: inspect persisted Soniox timing, prototype segmentation and absolute
   histograms, benchmark actual binary encodings and database cost, and measure precision, coverage,
   CPU/allocation, and replay accuracy. Produce the format/row budget and migration recommendation.
   No user-facing precision claim or automatic existing-data conversion yet.
   Initial [results](./speech-coach-measurement-storage-spike.md) include actual binary/database/WAL costs
   and a Soniox boundary regression. Accurate provider-word timing and real replay validation remain
   prerequisites; legacy token-derived maps must not be promoted to fine measurements.
   Follow-up backend foundation implemented: shared versioned measurement contract, actual-media bounds,
   nullable binary columns in existing Chat/Users rows, revision-bound capture/invalidation, preserved
   measurements during tagging, and reversible exclusion/tombstone behavior. Capture initially defaulted off
   and now defaults on at the developer's request; there is no historical backfill or timing-quality marker. Daily language rows now carry
   target-independent sparse histograms and coverage in a nullable binary column; period merges reuse
   `CoachDayBuilder.Merge`. Exact target classification still requires the stored entry segments.
2. Detail foundation: common navigation, explanations, calendar periods, speaking-day history, and
   existing filler/weak-word contexts. Add the shared chart only after confirming no existing fit.
   Implemented skill navigation, explanations, existing period controls, full per-language word lists,
   and kind/language-filtered occurrences with existing playback. Detail periods now use UTC Day,
   Monday–Sunday Week, and calendar Month with previous/next navigation and a captured request-time end.
   One period governs displayed totals, word lists, occurrences, and Pace distribution. Historical rows
   reuse daily aggregates; a current partial day reads only its finalized contributions before the cutoff.
   Speaking-day charts reuse `BarChart` with accessible day selection, measured zeroes, real dates and gap
   markers, and the existing configured word floor. Short days still contribute to weighted period totals.
   Pace history uses classified word/time totals, not histogram-bin centers or fabricated legacy measurements.
   Validation: 132 Users Coach unit, 47 Users Coach integration, seven focused Coach UI integration,
   and 46 localization/label tests passed (232 total); `npm run build:Verify`, mechanical style, and
   whitespace checks passed. Local browser checks confirmed month/week navigation, weighted values,
   compressed real-date gaps, 390-pixel layout, and 14-pixel chart labels in light/dark themes.
   The full CI build remains blocked by the pre-existing Core.Benchmarks solution
   filter mismatch. Personal baseline save/replace/clear and cutoff comparisons are the next slice.
3. Pace detail: versioned segments, aggregation of stored measurements, distribution, coverage, and review playback.
   Implemented exact inclusive target classification from stored segments, measured-audio coverage,
   and out-of-range playback moments with stale-map checks. Queries use a bounded 5,000-recording sample,
   disclosed when truncated. Historical unmeasured speech remains unavailable, not zero.
   Validation: 33 Core pace, 125 Users Coach unit, 114 Chat Coach unit, 45 Users Coach integration,
   six focused Coach UI integration, and two capture/lifecycle integration tests passed (325 total).
   `npm run build:Verify` passed; mechanical style and whitespace checks were clean. Local browser
   checks covered full word lists, occurrences, period switching, 390-pixel mobile layout, and
   light/dark unmeasured Pace states. Real-recording replay/alignment validation remains open.
4. Personal baselines: consistent snapshots, provenance/lifecycle, post-cutoff comparisons, and selection UI.
5. Final accessibility/localization/native verification and controlled rollout.

Create/link the implementation issue and feature branch before implementation; do not store a task link
on dev. This draft is a separate follow-up to the completed weekly-comparison PR, not a new scope for it.

### Decisions to settle before implementation

- Confirm candidate segment length/minimums and measured quality/coverage thresholds after the spike.
- Confirm whether segment pace remains explicitly distinct from existing headline pace, or approve a
  separately tested definition-alignment change. No silent score changes.
- Select timing-quality/language provenance, segment field precision, fixed histogram resolution,
  sparse/dense encoding and tail handling, and exact target-aggregation caching after measurement.
- Finalize versioned binary encoding, bounded baseline provenance, migration scope, and mixed-version
  rollout/rollback from the storage benchmark; row ownership and no-row-per-segment design are agreed.
- Agree the initial bounded history horizon, summary caching/rollups, and backfill budget.
- Confirm the baseline comparison's clipped-period wording and cutoff behavior for ongoing recordings.

The existing Recent design plan and earlier storage audit remain intact. Binary benchmarking is a
required gate for this feature, not deferred unrelated work. The initial spike does not authorize
production schema changes, automatic existing-data conversion, or user-facing precision claims.
