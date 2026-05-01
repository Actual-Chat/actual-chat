# Video Pipeline Refactoring Plan

This document tracks the incremental refactoring of the live video pipeline
toward the target design.

## Reference docs

- [video-pipeline.md](video-pipeline.md) — target high-level design.
  Conceptual stages, buffering and skipping policies, control plane
  structure, and the canonical `Constants.Video` block.
- [video-pipeline-now.md](video-pipeline-now.md) — current-state map.
  For each conceptual stage, names the matching files and classes today,
  describes how they work, and calls out the major differences from the
  target. Section 13.7 ranks the hardest control-plane refactorings.

## Completed steps

### Step 1 — Unified video pipeline constants

Established a single source of truth for video pipeline constants in .NET
and propagated them to every consumer.

- `Constants.Video` (`src/dotnet/Api/Constants.Video.cs`) is the canonical
  source. Added the doc's `FrameRate`, `FrameDuration`, `TargetBufferSize`,
  `TargetBufferDuration`, `KeyFramePeriod`, `KeyFramePeriodSize`,
  `BufferHysteresisSize`, `MinBufferSize`, `MaxBufferSize`,
  `ServerReplayTailDuration` fields. Renamed `StreamAckPeriod` →
  `RpcStreamAckPeriod`, `StreamBufferSize` → `RpcStreamBufferSize`,
  `ReplayBufferSize` → `ServerReplayTailSize` (values unchanged).
- `AppConstants` / `VideoConstants` (`src/dotnet/Api/AppConstants.cs`,
  `src/dotnet/Api/Video/VideoConstants.cs`) are the serializable DTOs;
  registered as a singleton in `ApiModule`.
- `BrowserInit` carries `AppConstants` to the main thread; each video
  worker receives them via an `init(appConstants)` RPC and populates a
  module-local `APP_CONSTANTS` / `VIDEO` field on first call (first-call
  wins).
- The shared module is `src/nodejs/src/app-constants.ts`. TS consumers
  read `VIDEO.frameRate`, `VIDEO.targetBufferSize`, etc. Reading before
  init throws — intended fail-loud behavior.
- Hardcoded TS literals (RpcStream `ackPeriod`/`bufferSize`,
  `SKIP_TO_LIVE_THRESHOLD_MS`, `LATENCY_REPORT_INTERVAL_MS`,
  `SLOW_DECODE_TIME_THRESHOLD_MS`, etc.) now read from `VIDEO.*`.

Outcome: changing one .NET constant updates every consumer everywhere.

### Step 2 — Anchor replay at the first keyframe

Ensured no consumer of the server stream store ever receives a delta
frame before its anchor keyframe.

- `VideoStreamingBackend.GetVideo` and `GetVideoRaw`
  (`src/dotnet/Streaming.Service/Backend/VideoStreamingBackend.cs`)
  apply `stream.SkipWhile(f => !f.IsKeyFrame)` between
  `_videoStreams.Get(...)` and the rest of the read pipeline.
- `AsyncMemoizer<T>` and `StreamStore<T>` are unchanged — they remain
  generic infrastructure shared across audio, video, and transcripts.
- `VideoStreamFilter`'s mid-stream `KeyFrameNumber` gap detection is
  unchanged and still serves as the safety net + observability signal.

Outcome: a single consumer-side filter guarantees every video read starts
at a keyframe, regardless of where in retention the consumer attaches
(initial join, late join, reconnect, p2p, simulcast).
