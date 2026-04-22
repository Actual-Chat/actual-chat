# Audio/Video Sync

How ActualChat keeps video aligned with audio during live streaming. Sync is
client-side, audio-clock-driven, and loosely coordinated with the server via
latency reports. There is no tight real-time A/V protocol across the wire.

## Key files

- `src/nodejs/src/audio-video-sync.ts` — `AudioVideoSync` registry (67 lines,
  the whole mechanism)
- `src/dotnet/UI.Blazor.App/Components/AudioPlayer/audio-player.ts:381` —
  audio pushes state into the registry on every feeder worklet state change
- `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts:684` —
  video reads the registry each render frame and picks a frame to display
- `src/dotnet/Api/Constants.Video.cs` — server/client tuning constants
- `src/dotnet/Streaming.Service/StreamLatencyStore.cs` — server-side
  latency aggregation and quality adaptation

## Clock source

Audio is the master clock. The `FeederAudioWorkletNode` emits state on every
change:

- `playingAtSec` — current audio playback position, seconds
- `recordedAtMs` — wall-clock ms-since-epoch when this audio was captured
- `playbackState` — `'playing' | 'paused' | 'ended' | 'starving'`

`AudioPlayer` forwards this to `AudioVideoSync.update(authorId, ...)`, which
stamps the state with `performance.now()` as `capturedAt`. Terminal state
`'ended'` clears the entry so video falls back to wall-clock timing.

## Video render loop (audio-sync path)

For each RAF, `VideoPlayer`
(`video-player.ts:684-745`):

1. Reads `AudioVideoSync.get(authorId)`.
2. Interpolates current audio position —
   `AudioVideoSync.interpolatePlayingAt(state) * 1000` extrapolates using
   `performance.now() - capturedAt` when state is `'playing'`.
3. Computes the target video offset:
   `targetVideoOffsetMs = (recordedAtMs − startedAtMs) + audioPlayingAtMs`.
   **No `pipelineLatencyMs` subtraction** — `recordedAtMs` already encodes
   end-to-end latency; double-counting would cause buffer bloat → render
   stall → SKIP_TO_LIVE spiral (see comment at `video-player.ts:688`).
4. Picks the matching frame from `pendingFrames`.
5. Logs drift once per second:
   `driftMs = lastRenderedOffsetMs − targetVideoOffsetMs`.

### Edge handling in audio-sync mode

- **Stale/negative target** (`video-player.ts:698-703`): if
  `rawTargetVideoOffsetMs < 0` (new stream after codec switch) or the target
  is >2s before the oldest buffered frame (stale audio state after
  SKIP_TO_LIVE), snap to the latest buffered frame.
- **Buffer safety cap** (`video-player.ts:708-734`): even in audio-sync mode,
  if the buffer span exceeds 2s, flush frames older than the target to
  prevent unbounded latency growth from bursty delivery.

## Wall-clock fallback (no audio peer)

When `AudioVideoSync.get()` returns `undefined`, `VideoPlayer` advances from
`playbackStartTime` and adapts `playbackRate` based on buffer depth
(`video-player.ts:747` onward):

- `bufferSpan ≥ CATCHUP_GENTLE_MS` (300ms) → 1.05×
- `bufferSpan ≥ CATCHUP_AGGRESSIVE_MS` (1000ms) → 1.15×

## Graduated latency recovery

Applies to both paths (`video-player.ts:1356-1400`). Runs per latency-report
tick; skipped during a 5s cooldown after a SKIP_TO_LIVE.

| Phase | Trigger | Action |
| --- | --- | --- |
| 1 | `CATCHUP_GENTLE_MS < latency ≤ DROP_TO_KEYFRAME_MS` (300–2000 ms) | Reduce `pipelineLatencyMs` by `min(excessMs * 0.3, 20)` per tick, advancing the audio-sync target gradually |
| 2 | `DROP_TO_KEYFRAME_MS < latency ≤ SKIP_TO_LIVE_THRESHOLD_MS` (2000–3000 ms) | Drop the oldest 50% of `pendingFrames` (no isKeyFrame metadata on decoded frames, so half-drop instead of keyframe-aware drop) |
| 3 | `latency > SKIP_TO_LIVE_THRESHOLD_MS` (> 3000 ms) | Abort the current stream fetch, report latency to server, re-request stream from live offset. One-shot per 5s cooldown. |

## Tuning constants

TypeScript constants (`video-player.ts:54-59`):

| Constant | Value | Purpose |
| --- | --- | --- |
| `SKIP_TO_LIVE_THRESHOLD_MS` | 3000 | Client-side live-rejoin trigger (mirrors `Constants.Video.SkipToLiveThresholdMs`) |
| `CATCHUP_GENTLE_MS` | 300 | Start gentle 1.05× catch-up / phase-1 reduction |
| `CATCHUP_AGGRESSIVE_MS` | 1000 | Escalate wall-clock catch-up to 1.15× |
| `DROP_TO_KEYFRAME_MS` | 2000 | Phase-2 buffer drop trigger |

Server/client shared constants (`Constants.Video.cs:24-29`):

| Constant | Value | Purpose |
| --- | --- | --- |
| `LatencyReportInterval` | 2 s | Peer → server latency reporting cadence |
| `HighLatencyThresholdMs` | 900 ms | Server-side: triggers quality step-down |
| `LowLatencyThresholdMs` | 300 ms | Server-side: allows quality step-up |
| `SkipToLiveThresholdMs` | 3000 ms | Client-side: re-request stream from live |
| `QualityDecisionInterval` | 2 s | Server quality re-evaluation window |
| `QualityHysteresisWindow` | 5 s | Cooldown before server step-up |

## Server-client interplay

There is **no server-side clock alignment**. The server only receives
latency reports and adjusts encoder quality.

Reporting (`video-player.ts:1230` onward): `VideoPlayer` calls
`ReportPeerLatency(streamOffsetMs, decodeTimeMs, bufferDepth, bufferSpanMs)`
every `LatencyReportInterval`. The server computes end-to-end latency as
`ServerClock.now() − (startedAt + streamOffsetMs)` and buckets peers by
network- vs. receiver-bound slowness.

Adaptation (`StreamLatencyStore.cs:187-309`): every 2s,

- **Step down** if >50% of peers (>34% in small calls) exceed 900 ms, or
  throughput < 50% of target.
- **Step up** if all peers < 300 ms and 5s hysteresis has elapsed.
- New quality is routed via `ObserveStreamQualityRequests()` to the sender's
  `RecordingService.reconfigure()`.
