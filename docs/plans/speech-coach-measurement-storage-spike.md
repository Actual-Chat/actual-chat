# Speech Coach measurement and storage spike

Issue: [#5128](https://github.com/Actual-Chat/actual-chat/issues/5128). Branch: `feat/coach-skill-details`.

This is the initial implementation gate for
[skill details and personal baselines](./speech-coach-skill-details-and-baselines.md).
**The fine-timing rollout gate is not cleared.** New Soniox timing is captured in the existing map,
and timestamp corrections survive transport. Timing metadata, its binary codec/column, and the local
migration were removed at the developer's request. No replacement timing marker was added.
The follow-up adds nullable binary pace-measurement columns to existing Coach rows. Capture initially
was disabled by default and now defaults on at the developer's request. Fillers, Weak Words, and Pace
details are implemented; Pace target percentages are exact for the stored segments, not a claim of
provider alignment accuracy. Nothing has been deployed, and there is no historical measurement backfill.

## Implemented prototype

- Dependency-free timed-word validation, non-overlapping pace segmentation, coverage accounting,
  absolute duration-weighted histograms, and exact range classification in `Core/Audio`.
- Candidate defaults: 10-second segments, at least 5 words and 3 seconds, split at pauses of at least
  1 second. An undersized tail can merge only within its block and up to twice the target duration.
  These remain prototype defaults, not validated production thresholds.
- `SpeechPaceStats` adapts playable text using the existing lexical policy. Detailed measurements
  require an explicit timing-quality assertion; missing provenance, whole-recording fallback maps,
  invalid map geometry, unsupported word segmentation, and invalid duration remain unavailable.
- Existing headline formulas and historical maps are unchanged. New Soniox maps retain precise word
  boundaries in the original `TimeMap`, so new headline inputs can improve. Lexical word-range extraction
  is shared with `SpeechTextStats` without changing its counting policy.
- Repeatable serializer benchmarks, real existing-payload round trips, histogram-size diagnostics,
  a legacy-map corpus probe, and an isolated PostgreSQL storage/WAL experiment.

### Reuse

Existing abstractions: `Range<int>`, `LinearMap`, `PlayableTextMarkup`, `SpeechTextStats`,
`SpeechTimingStats`, `SonioxTranscriptBuilder`, `CoachRecord`, `CoachDay`, `SpeechSpan`,
`VersionedByteSerializer`, and the application's MessagePack/SystemJson serializers.

Substantial primitive math is shared in Core rather than duplicated in Coach services or the UI.
Markup adaptation remains beside the existing Api speech statistics. Storage candidates and the
packed-integer experiment stay in `tests/Benchmarks`; a production reusable codec could belong in
Core/Serialization, but these prototype readers are not production contracts or a new serializer framework.
`SpeechPaceMeasurement` and `SpeechPaceSummary` belong in shared Api rather than the Users service:
both transport/domain contracts are needed by services and the future UI. Dependency-free histogram
math stays in Core; no feature-local duplicate aggregation service or serializer framework is added.

## Disabled-by-default capture and persistence

The backend follow-up now connects the existing measurement algorithm to finalized-entry analysis:

- `SpeechPaceMeasurement` in Api carries the algorithm version, actual media duration, segments, and
  coverage. It uses the existing versioned MessagePack serializer and rejects unsupported versions,
  invalid ranges/counters, trailing payloads, and inconsistent word/time coverage.
- Generated `AddCoachPace` migrations add nullable `pace_data bytea` columns to `coach_entries` and
  `coach_events`. A generated `AddCoachDailyPace` migration adds the same nullable binary field on
  `coach_days`. There are no new tables, raw timing maps, or timing-quality markers.
- `ChatSettings.Coach.IsPaceEnabled` defaults to false. Newly finalized entries are the only automatic
  capture path. Commands bind capture/invalidation to the entry revision; stale commands cannot
  overwrite newer measurements. Ordinary entry/conversation tagging preserves current measurements.
- Bounds come from `Media.DurationMs`, not shortened entry content duration. Text/audio edits and
  remapping clear the measurement rather than treating remapped timing as accurate. Removing audio
  drops the analysis. Exclusion retains the contribution for reversibility; tombstones erase it and
  advance their revision so intermediate stale events cannot resurrect it.
- Existing headline inputs and formulas, historical records, and historical maps are not backfilled.
  Users contribution JSON excludes the measurement; its single binary column restores it for readers.

Enabling capture is an operator assertion about the source pipeline, not evidence of timing accuracy.
Exact structural boundaries cannot establish provider accuracy or identify legacy/DTW-remapped maps.
The accuracy/replay gate remains open, and no user-facing pace-detail precision claim is authorized.
Daily language histograms and period merges are implemented below. Detail/history UI and baselines
remain unimplemented. Capture must stay off during mixed-version deployment until all contribution
and daily-row writers support the binary fields; an older writer cannot keep the new aggregates current.

### Actual measurement contract cost

These figures use the new production measurement codec, not the earlier flattened candidate.
The PostgreSQL experiment inserts 5,000 identical samples per size into disposable local tables with
one primary-key index; replacement rewrites the same payload. All disposable databases were removed.

| Segments | JSON B | MessagePack B | Stored column B | Allocated B/row | Insert WAL B/row | Replacement WAL B/row |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 281 | 26 | 27 | 101.58 | 156.00 | 239.30 |
| 6 | 726 | 102 | 103 | 175.31 | 232.00 | 315.30 |
| 60 | 5,739 | 1,186 | 1,190 | 1,399.19 | 1,319.00 | 1,402.30 |
| 360 | 34,751 | 7,188 | 7,188 | 8,383.69 | 7,867.59 | 8,167.02 |

Production-codec CPU/allocation (BenchmarkDotNet in-process ShortRun, three measured iterations):
six-segment encode/decode averaged 284/325 ns with 168/248 allocated bytes; sixty-segment encode/decode
averaged 2.25/2.46 microseconds with 1,256/1,544 allocated bytes. These are local microbenchmarks,
not database or full-pipeline latency claims.

Six segments in the Chat and Users copies together are 204 raw binary bytes or 206 column bytes.
Standalone-table allocation is about 351 bytes and insert WAL about 464 bytes for the two copies;
these are **not** incremental production-row or complete end-to-end operation-log measurements.
The report includes the actual contract and verifies its semantic binary round trip. Denser `TimeMap`
growth, event/log serialization, retries, replicas, backups, and daily distributions are still separate
budget items. No historical JSON conversion is performed by these migrations.

## Daily language aggregation

`SpeechPaceSummary` is a shared Api contract using the existing histogram builder and versioned
MessagePack serializer. It stores absolute 5-WPM sparse duration bins, measured-entry count, actual
audio duration, valid/rejected/unclassified word counts, and unclassified/pause/unmapped milliseconds.
Counters are 64-bit, merges are checked for overflow, and readers reject invalid or unsupported data.

`CoachDayBuilder.BuildAll` keeps the existing ISO-language grouping; neutral run rows do not duplicate
pace. `Build` aggregates eligible entry measurements and `Merge` combines daily/period summaries.
Missing measurements remain null. A measured entry with no classifiable segments has empty bins but
retains its coverage. Inactive days add nothing. Headline counts, weighted totals, and scoring are unchanged.
Exact arbitrary-target classification remains a segment operation: bins are not used to invent an exact
below/within/above percentage when a target cuts through a bin.

`DbCoachDay.PaceData` is the only new daily field; JSON omits the aggregate to avoid duplicate storage.
Existing contribution filtering and daily rebuilds handle exclusions, removals, replacement, language
moves, and explicit rebuilds. No historical entry measurements are generated or backfilled.

### Daily aggregate storage and write cost

Synthetic production-contract samples, with semantic codec round trips and 5,000-row PostgreSQL probes:

| Source segments | Occupied bins | JSON B | MessagePack B | Stored column B |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 1 | 231 | 21 | 22 |
| 6 | 2 | 243 | 25 | 26 |
| 60 | 23 | 473 | 115 | 116 |
| 360 | 25 | 519 | 169 | 173 |

A second probe uses daily-row-shaped tables: user/language/day composite primary key, revision,
complete synthetic `CoachDay` JSON, and the nullable binary field. Comparing null and populated
fields in the same schema isolates payload cost (not the migration's nullable-column/null-bitmap cost):

| Occupied bins | Added allocated B/row before replacement | Added insert WAL B/row | Added replacement WAL B/row |
| ---: | ---: | ---: | ---: |
| 1 | 0 | 22 | 22 |
| 2 | 0 | 26 | 26 |
| 23 | 147.46 | 116 | 116 |
| 25 | 147.46 | 175 | 175 |

Zero allocation growth in the small samples reflects existing page capacity, not free storage.
These are synthetic local row probes, not complete application operation-log/replica/backup costs.
All disposable databases were removed. Reproduce with the report's `dailySummary.dayBase64` and
`dailySummary.base64`, and `SpeechPaceStorage.ps1 -WithDailyRow` using null (`none`) and binary formats.

Local ShortRun CPU/allocation: 2-bin encode/decode averaged 94/145 ns and 128/640 allocated bytes;
23-bin encode/decode averaged 166/380 ns and 216/1,536 bytes. Merging 100 summaries averaged
2.71 microseconds / 13,816 bytes for 2 bins and 20.82 microseconds / 15,936 bytes for 23 bins.
ShortRun confidence intervals are broad; these figures are microbenchmark evidence, not latency promises.

Validation for this slice: 124 Users Coach unit tests (including 8 new aggregate tests), 43 Users Coach
integration tests, 114 Chat Coach unit tests, and migration parity passed. Tests cover language separation,
legacy/null and unclassified coverage, period merge associativity, binary/JSON/MessagePack round trips,
invalid/unsupported data, overflow, replacement with unchanged headline metrics, language moves,
rebuilds, exclusion/restore, invalidation, and deletion/tombstones. The focused Chat capture/remapping/
audio-removal integration case also passed. Mechanical style and whitespace checks are clean; EF reports
no pending model changes. The full CI solution build remains blocked by the pre-existing filter entry
for `tests/Core.Benchmarks/Core.Benchmarks.csproj`, which is absent from `ActualChat.sln` (MSB5028).

## Timing evidence

Follow-up [existing dev audio validation](./speech-coach-dev-timing-validation.md) uses 100 mapped
entries and seven locally decoded recordings. It confirms rich historical data exists, identifies
interpolated boundaries and content-vs-blob duration differences, and tests an opt-in detailed map
without changing legacy playback. English audio is not an implementation blocker.

A reproducible Soniox builder fixture has 25 words, each 400 ms long, and a 2-second pause between
words 12 and 13. The token times retain the pause: 4,800 ms end, 6,800 ms next start. The legacy map
reports **zero qualifying pauses**. Consecutive tokens share a text boundary; the next start point
is discarded, and a leading space is subsequently interpolated across the gap.

Consequently, a token-derived legacy map is not itself proof of accurate word boundaries. Promoting
these maps to fine pace would classify this example from a 12-second span instead of its 10 seconds
of speech. The `HistoricalEndOnlyMapShouldNotBeTrustedForFinePace` fixture explicitly constructs the old map
shape and verifies the quality gate; new Soniox transcripts instead preserve the pause in one map.

The read-only local corpus contained 65 Coach entries with at least 5 words:

| Diagnostic | Result |
|---|---:|
| Entries passing structural probe checks | 47 |
| Entries eligible under the diagnostic's false provenance assertion | 0 |
| Probe segments | 77 |
| Probe rejected words | 154 |
| Probe unclassified words | 143 |
| Probe classified duration | 654,747 ms |
| Total recording duration | 953,680 ms |

The approximately 68.7% probe coverage is **not an accuracy result**. Historical rows lack the required
provider/alignment provenance, and the fixture disproves an assumption about the existing map.
The eligibility count is a conservative diagnostic policy, not proof that all historical maps are
inaccurate. Follow-up Russian audio diagnostics are recorded separately. Recognition accuracy,
edits, offline corrections, and code switching still need validation against an accurate word source.

### Next timing decision

Approved simplification: improve the original `TimeMap` for new recordings and leave historical maps
untouched. The developer also removed compact timing metadata and its database column; do not replace
it with another marker. Use existing audio bounds and the utterance language. Persist only one map.
Existing formulas remain unchanged, but better new inputs may change their outputs. Never promote
legacy or DTW-remapped playback timing to precise measurements just because the map is dense.

## Serializer measurements

Actual application MessagePack serialization is wrapped in `VersionedByteSerializer`; algorithm
version is a separate field. All four candidate segment formats round-trip metadata and measurements.
The candidate contains language, timing quality, recording duration, text/audio bounds, word counts,
and speech duration. It excludes future baseline provenance and any additional fine-timing source.

| Segments | Named JSON bytes | Versioned MessagePack bytes | MessagePack LZ4 bytes | Packed delta bytes |
|---:|---:|---:|---:|---:|
| 0 | 96 | 9 | 9 | 8 |
| 1 | 210 | 22 | 22 | 17 |
| 6 | 807 | 103 | 114 | 61 |
| 60 | 7,471 | 1,239 | 1,162 | 545 |
| 360 | 45,662 | 7,539 | 6,832 | 3,228 |

Six segments are approximately one minute; generated recording durations also include gaps.
Packed deltas use .NET's 7-bit integer support, not a new shared varint implementation.
Brotli Fastest was measured separately: 6-segment MessagePack grows from 103 to 107 bytes; at
60 segments it shrinks to 959 bytes, and packed data shrinks to 427 bytes. This is exploratory,
not a proposed compression layer.

### CPU and allocation

BenchmarkDotNet ShortRun, in-process .NET 11 RC, AMD Ryzen 9 9950X. Three measurement iterations
are directional evidence, not a portable latency guarantee.

| Operation | 6 segments, mean / allocation | 60 segments, mean / allocation |
|---|---:|---:|
| JSON encode | 664 ns / 2,784 B | 5,240 ns / 22,776 B |
| JSON decode | 1,977 ns / 4,008 B | 15,105 ns / 31,696 B |
| MessagePack encode | 126 ns / 168 B | 909 ns / 1,304 B |
| MessagePack decode | 192 ns / 392 B | 1,376 ns / 2,984 B |
| LZ4 encode | 246 ns / 184 B | 1,720 ns / 1,232 B |
| LZ4 decode | 323 ns / 392 B | 1,665 ns / 2,984 B |
| Packed encode | 129 ns / 472 B | 951 ns / 2,544 B |
| Packed decode | 188 ns / 512 B | 1,615 ns / 3,104 B |

Plain MessagePack is the initial recommendation for ordinary short entries: substantially smaller
and cheaper than named JSON, without a custom decoder. LZ4 is not worthwhile for the short sample.
Packed deltas save bytes, particularly on long entries, but do not improve this implementation's
allocation profile; production validation, compatibility, corruption handling, and maintenance cost
must justify adopting them.

### Timing-source payloads are a separate cost

The discarded two-map design measured 2,159.22 named-JSON bytes versus 2,083.04 MessagePack bytes
per entry on 100 historical samples (19,670 points). Its approximately 3.5% savings did not justify
persisting a duplicate map. Those numbers describe the discarded contract, not current storage.

The subsequently discarded metadata-only design measured version, text/map hashes, unconfirmed state,
audio duration, and a representative media identifier. Its historical measurements are retained below,
but the contract, codec, database column, and `speech-timing-source` command have now been removed.

| 100 discarded metadata payloads | Named JSON | Versioned MessagePack |
|---|---:|---:|
| Total bytes | 25,395 | 13,514 |
| Mean bytes per entry | 253.95 | 135.14 |

The existing JSON `time_map` remains the sole stored map. New precise maps may retain more points,
so their growth must be included in full-flow heap/TOAST/WAL and transport/log measurements. The
segment-only PostgreSQL measurements below do not cover those costs.

### Absolute histograms

Five-WPM bins preserve actual duration and absolute positions, not deviations from a target.
Sparse MessagePack sizes for 6/60/360 segments are 16/102/154 bytes; trimmed dense representations
with an absolute first-bin offset are 20/84/132 bytes. Dense saves little in these examples. Sparse
is a reasonable initial representation because unusual tails must not allocate a huge zero-filled range.
Exact target-boundary totals are still computed from segments, never interpolated inside a bin.

## PostgreSQL measurements

The repeatable PowerShell experiment creates an isolated temporary-named **database**, uses logged
tables with a bigint primary key, inserts 5,000 copies of each sample, measures each statement's WAL
with `EXPLAIN (ANALYZE, WAL, BUFFERS)`, replaces each payload once, and drops the database in `finally`.
Production/local application tables are not modified. Database cleanup was checked.

Default PostgreSQL storage/compression, one payload column, one primary-key index:

| Segments / format | Stored column B | Allocated B/row before update | Insert WAL B/row | Replacement WAL B/row |
|---|---:|---:|---:|---:|
| 6 JSONB | 1,246 | 1,399 | 1,375 | 1,458 |
| 6 MessagePack | 104 | 175 | 233 | 316 |
| 6 LZ4 | 115 | 192 | 244 | 327 |
| 6 packed | 62 | 134 | 191 | 274 |
| 60 JSONB | 1,688 | 2,081 | 1,817 | 1,900 |
| 60 MessagePack | 1,243 | 1,399 | 1,372 | 1,455 |
| 60 LZ4 | 1,166 | 1,399 | 1,295 | 1,378 |
| 60 packed | 549 | 664 | 678 | 761 |
| 360 JSONB | 8,525 | 9,090 | 9,338 | 9,691 |
| 360 MessagePack | 7,539 | 8,384 | 8,219 | 8,518 |
| 360 LZ4 | 6,832 | 8,384 | 7,512 | 7,811 |
| 360 packed | 1,833 | 2,081 | 1,962 | 2,045 |

Allocated bytes include heap, TOAST/auxiliary storage, and indexes. Small tables have fixed page
costs. Replacing all rows grows dead-row storage before vacuum; it approximately doubles allocation
in this experiment. PostgreSQL compresses large JSONB and some packed payloads, so raw serializer
ratios are not physical-database ratios. Long MessagePack/LZ4 values use TOAST; this explains why
LZ4's smaller long payload does not reduce the measured allocated-page count here.

These are isolated payload-table measurements, not the exact incremental cost of adding a nullable
column to the existing Coach schema, its composite identifiers, and its additional indexes.

### Full current payloads and incremental feature budget

The current read-only audit has 85 Chat entry rows, 13 conversation rows, 98 Users events, and 11
language/day rows. Live row bytes total 155,227; allocated relations/indexes total 491,520 bytes.
Chat entry rows average 528 bytes and corresponding Users entry-event rows average 1,022 bytes:
approximately **1,550 live-row bytes per message across the two existing copies**, before daily/run
amortization, logs, audio, replicas, or backups.

Actual existing model serialization, including real spans and metadata, was round-tripped through the
application serializer; semantic JSON equality is checked independently of object-property order:

| Existing payload | Rows | Mean JSON B | Mean versioned MessagePack B |
|---|---:|---:|---:|
| Users entry contribution | 85 | 719 | 155 |
| Users run contribution | 13 | 419 | 111 |
| Users daily aggregate | 11 | 809 | 210 |
| Chat spans | 85 | 381 | 98 |

These measured savings replace the earlier hand-calculated binary estimates. They do not authorize
conversion of existing JSON fields or establish final table sizes after migration.

For the six-segment prototype, two authoritative/contribution copies add **206 raw binary bytes**
(or about 208 stored-column bytes in this experiment) per approximately one-minute entry. The
standalone two-row table analogue uses about 350 allocated bytes and 466 insert-WAL bytes.
Actual additive column cost differs because existing row/index overhead is already paid. The new
histogram adds about 16 bytes per occupied language/day payload in this small example, not a row
per segment or bin.

Full new-message cost is existing storage plus new segment/timing fields, amortized day aggregates,
and optional baseline provenance. The current candidate measures only the segment/histogram increment;
the discarded source-contract benchmarks above are not current persisted increments.
Baseline provenance and full map/contribution-flow database costs still need final measurement.
Operation/event logs remain JSON: typed data may repeat named payloads, while byte arrays become base64
(about 4/3 of binary size, plus keys/envelope). Do not count binary database savings as log savings.
WAL, replicas, retained backups, retries, day rebuilds, and mixed-version dual writes multiply or retain
these costs; the statement WAL above measures the payload experiment, not the entire contribution flow.

## Migration recommendation

1. Prefer a versioned MessagePack field for new measurements, conditional on the fine-source gate.
2. Keep algorithm version, format version, timing quality, and language provenance distinct.
3. Keep legacy measurements unavailable rather than turning missing/corrupt/unsupported data into zero.
4. Preserve existing JSON reads and source version/tombstone/exclusion rules during any rollout.
5. Decide existing JSON conversion separately. Real serialization savings are substantial, but a
   migration still needs generated EF changes, mixed-deployment handling, backfill, rollback, and
   measurement of transient duplication and full contribution-flow write amplification.
6. Do not add per-segment or per-bin rows. Keep historical maps unchanged; new playback maps retain
   precise boundaries, with the approved consequence that new headline inputs may improve.

## Reproduction and validation

```powershell
dotnet test tests/Core.UnitTests/Core.UnitTests.csproj --filter 'FullyQualifiedName~SpeechPace'
dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --filter 'FullyQualifiedName~SpeechPaceStatsTest|FullyQualifiedName~SpeechTimingStatsTest|FullyQualifiedName~SpeechTextStatsTest'
dotnet test tests/Transcription.UnitTests/Transcription.UnitTests.csproj --filter 'FullyQualifiedName~SonioxTranscriptBuilderTest'
dotnet run --project tests/Benchmarks/Benchmarks.csproj -c Release -- speech-pace-report
dotnet run --project tests/Benchmarks/Benchmarks.csproj -c Release -- --filter '*SpeechPaceStorageBenchmarks*' --artifacts tmp/coach-skill-details/benchmark-results
pwsh -NoProfile -File tests/Benchmarks/SpeechPaceStorage.ps1 -SamplesPath tmp/coach-skill-details/storage-samples.json
```

`storage-samples.json` is an array of the JSON report lines, excluding build output.
`speech-pace-corpus <path>` accepts the read-only corpus export; `speech-pace-existing <path>` accepts
existing Coach event/day/span payloads. Private source exports remain under ignored `tmp`, not in git.

After removing metadata: 180 transcription, 106 Coach, 33 Core pace, and one migration-parity test
pass. EF reports no pending model changes; style and whitespace checks pass. The earlier
metadata-persistence suite was removed with the contract; map precision/transport/legacy JSON tests remain. App/Streaming compiled
as dependencies of these tests. Storage candidates and actual existing models passed binary round trips.
Follow-up persistence validation: 114 Chat Coach unit tests, 116 Users Coach unit tests, 42 Users
Coach integration tests, four distinct focused Chat integration cases, and migration parity passed.
Both migration projects report no pending model changes. Mechanical style and whitespace checks pass.
The broad Chat Coach integration run exceeded its four-minute command limit without a reported failure;
focused capture, default-off, edit, and redelivery cases passed separately.
Browser replay validation has not been repeated. UI, baseline lifecycle, replay accuracy, daily
native rollout remain later work. Daily aggregation validation above supersedes the earlier
pre-aggregation counts.
