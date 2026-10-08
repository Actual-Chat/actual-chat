# Speech Coach: existing dev timing validation

Issue: [#5128](https://github.com/Actual-Chat/actual-chat/issues/5128).
Related: [measurement/storage spike](./speech-coach-measurement-storage-spike.md).

## Scope and safety

The developer approved using existing Russian recordings rather than waiting for additional English
samples. Timing-map construction is language-independent; word segmentation and comfortable pace ranges
remain language-specific. English audio is not an implementation blocker, but this sample does not
establish English recognition accuracy.

Queries used `agent-read`, `default_transaction_read_only=on`, and a 15-second statement timeout.
Application data was read only from `ac_dev_chat` and `ac_dev_media`. Seven selected audio blobs were
read from `actual-chat-app-dev-blobs`, decoded locally, and kept under ignored private `tmp` paths.
No audio was sent to Soniox or another transcription service. No transcripts, scores, playback maps,
media records, or production application data were modified.

## Existing corpus

The specified chat contains 1,437 Coach entries: 1,431 Russian entries and six short English entries.
A duration-ranked sample of 100 mapped entries contains 57.95 minutes, 7,791 Coach words, and 19,670
map points. All 100 have increasing text offsets and nondecreasing timestamps; none are two-point
whole-recording fallback maps.

The initial segmentation probe classified 3,349,176 of 3,477,180 ms: approximately 96.3% recording
coverage, with 144 rejected and 43 unclassified words. This is structural/coverage evidence, not
word-alignment accuracy. The diagnostic's zero provenance-eligible count reflects its deliberately
false detailed-timing assertion; it is not an independently measured failure rate.

## Audio and boundary audit

Seven recordings, totaling 535.08 entry-duration seconds, were selected for length and reported pauses.
They contain 1,154 lexical words. Decoded blob durations differ from entry durations: an entry's
content timeline is not necessarily the complete stored audio extent.

| Entry | Words | Exact map word starts | Exact map word ends | Entry seconds | Decoded blob seconds |
|---:|---:|---:|---:|---:|---:|
| 213908 | 198 | 3 | 198 | 87.000 | 87.513 |
| 213784 | 192 | 180 | 0 | 84.480 | 84.933 |
| 213779 | 216 | 2 | 216 | 84.360 | 85.974 |
| 214000 | 190 | 3 | 190 | 84.120 | 84.534 |
| 213427 | 148 | 1 | 148 | 80.040 | 80.474 |
| 214672 | 93 | 1 | 93 | 60.000 | 60.440 |
| 213739 | 117 | 1 | 117 | 55.080 | 55.453 |

The audit uses the existing playable-word regex and lexical counting policy. A boundary is exact when
its UTF-16 text offset is a stored map point; otherwise its time is interpolated. Six samples retain
all word ends but only 1–3 exact word starts. The seventh has a different, predominantly start-point
shape. Provider/alignment provenance cannot be inferred reliably from map density alone.

Local FFmpeg silence detection was run at -30, -40, and -50 dB with a 400-ms minimum interval.
Examples of internal quiet intervals at the conservative -50 dB threshold:

| Entry | Acoustic interval, seconds | Acoustic duration | Nearby mapped inter-word gap | Mapped duration |
|---:|---|---:|---|---:|
| 214000 | 53.407–55.129 | 1.722 s | 53.610–54.110 | 0.500 s |
| 214672 | 0.917–2.847 | 1.931 s | 1.050–1.690 | 0.640 s |
| 214672 | 47.170–49.044 | 1.873 s | 47.310–48.180 | 0.870 s |

These diagnostics show that structural map validity does not establish pause fidelity. They are
consistent with interpolated starts shortening gaps below the one-second split threshold.
Silence detection is amplitude-based: background noise, unvoiced consonants, timestamp uncertainty,
and transcript errors affect it. It is not forced word alignment or a human listening accuracy score.
The nearest-gap comparison is a diagnostic, not proof of the semantic identity of adjacent words.

### Recording-boundary finding

Several maps end about 30 ms beyond the Coach entry duration; entry 213908 ends 150 ms beyond it.
Those times are still within the decoded blob. Using the entry's shortened duration as a hard audio
bound can reject otherwise in-bounds words. Fine measurements must use the correct media-relative
clock and actual audio extent, with a separately declared speaking/content duration. This must not
silently alter the existing headline formula.

## Single-map implementation

The developer approved improving the existing `TimeMap` for new recordings rather than retaining a
second map. `SonioxTranscriptBuilder.CreateDetailedTimeMap` constructs that map from token timestamps:

- Map token starts after leading whitespace and ends before trailing whitespace.
- Start with an empty map so a delayed first word retains its actual start time.
- Preserve subword boundaries and skip endpoint markers without consuming text offsets.
- Reject invalid or backward word timing rather than asserting precision from a partial map.

Both realtime updates and completion retain trimmed boundaries in the existing map. Historical rows
are not rewritten. Playback still uses the same representation; headline formulas remain unchanged,
but new recordings can yield different speech/pause metrics because their inputs are more accurate.
Nothing has been deployed. This does not promote historical maps to precise measurements.

### Local single-map stage

- The developer explicitly removed the timing metadata object, binary codec, storage column, and
  migration. There is no replacement persisted quality marker and no new database schema.
- Soniox completion copies provider tokens and builds the existing `TimeMap` from their timestamps.
  Invalid detailed maps fall back to the valid incremental playback map.
- `TranscriptDiff` keeps exact timestamp changes rather than dropping corrections below 100 ms.
  Existing transcript and audio DTOs have no new metadata fields.
- Soniox's current realtime silent prefix is zero; offline Soniox uses the same builder on final tokens.
  Existing refinement and playback-remapping paths remain unchanged.
- Lexical boundaries must be exact map points rather than interpolated timestamps in the prototype
  pace adapter. This structural check is not proof of recognition/alignment accuracy. Without persisted
  provenance, legacy/remapped maps cannot be automatically distinguished from precise provider timing.
- Attribution uses the utterance's language; mixed language does not block segmentation. Unsupported
  word segmentation remains unavailable. Accurate audio bounds still come from existing media data.

Fine pace is not yet connected to persisted Coach contributions. Fine-accuracy/replay validation,
full-flow PostgreSQL costs, and the rollout gate remain open. The discarded duplicate-map/metadata
cost experiments are recorded in the [storage spike](./speech-coach-measurement-storage-spike.md).

The 25-word regression now uses the single precise map: the raw two-second pause is retained,
classified speech totals ten seconds, and both speech blocks measure 150 WPM. English/Russian
fixtures preserve leading/trailing silence, and a subword/whitespace fixture retains boundaries.
Negative and backward word timings are rejected.

### Reuse and placement

Reuse `SonioxTranscriptBuilder`, its endpoint handling, `LinearMap`, and `TryAppend`; reuse
`PlayableTextMarkup`, `SpeechTextStats`, `SpeechTimingStats`, and the Core pace math in tests.
The source-specific factory stays in the existing Soniox builder instead of introducing a new
provider service or generic Core token framework. Any provider-independent timing math continues
to belong in Core; this method consumes Soniox's own token model.

## Implementation consequences

- Existing dev audio is sufficient to investigate the Russian timing mechanism. No new recordings
  or bulk retranscription are required for this stage.
- Missing provenance alone is not proof that every historical map is inaccurate. Keep source quality
  explicit and validate eligible historical representations rather than blanket-claiming precision.
- Do not use the unmodified legacy word map to claim exact pause-aware segment pace.
- Preserve historical entries and existing score formulas. Improved new maps may change new scores.
- Keep media-relative timing, source revision, and refinement/edit behavior explicit. Positional language
  handling is deferred; this iteration uses the entry language.
- JSON-to-MessagePack conversion remains independent and lossless; it can preserve historical summary
  data without fabricating detailed timing.

Private artifacts: `tmp/coach-skill-details/dev-audio-validation-samples.json`, `dev-word-boundaries.json`,
`dev-acoustic-audit.json`, and `dev-audio/`. They are diagnostic inputs, not tracked fixtures.
