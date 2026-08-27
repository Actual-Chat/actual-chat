# 09 — Diagnostics, observability, and tunables

This doc collects what's measured, where it surfaces, and which constants you
might want to know about when debugging or tuning.

## Server-side meters

Files: `src/dotnet/Core.Server/Diagnostics/AppMeters.cs`,
`src/dotnet/Streaming.Service/Diagnostics/StreamingMeters.cs`.

| Instrument | Kind | Source of values |
|---|---|---|
| `VideoStreamCount` | `UpDownCounter<int>` | `StreamStore` publish/expire on each backend pod |
| `VideoLatency` | `Histogram<double>`, ms | `ChangePlaybackQuality(info)` per stream (`LatencyMsEma`); tagged primary/secondary |
| `VideoSendEncodeRatio` | `Histogram<double>` | `ChangeRecordingQuality(info.Health.EncodeRatioP90)` |
| `VideoSendDropRatio` | `Histogram<double>` | `info.Health.SenderFrameDropRatioEma` |
| `VideoSendAckAgeMs` | `Histogram<double>` | `info.Health.LastAckAgeMs` (skipped when -1) |
| `VideoSendLayerCount` | `Histogram<int>` | `state.EffectiveLayerCount` |
| `VideoReceiveCapacityBps` | `Histogram<long>` | `ChangePlaybackQuality(info.EstimatedCapacityBytesPerSec)` |
| `VideoReceiveAggregateHealth` | `Histogram<double>` | `info.AggregateHealth` |
| `VideoReceiveKeyframeSkips` | `Counter<long>` | `info.Streams[*].KeyframeSkipsInWindow` |
| `VideoReceiveDecoderQueue` | `Histogram<double>` | `info.Streams[*].DecoderQueueDepthEma` |
| `VideoFrameDeserializeDuration` | `Histogram<double>`, µs | `CachingVideoFrameFormatter` deserialize |
| `VideoFrameSerializeDuration` | `Histogram<double>`, µs | same, serialize |
| `VideoFrameSizeBytes` | `Histogram<int>` | encoded chunk size |
| `VideoActiveConsumers` | `UpDownCounter<int>` | `LiveVideoStreams.GetStream` enter/exit |
| `VideoFramesReceived` / `VideoBytesReceived` | `Counter<long>` | publish path |
| `VideoFramesSent` / `VideoBytesSent` | `Counter<long>` | per-consumer fan-out |

Routing into the OTEL collector and Grafana is in
`Core.Server/Diagnostics/AppMeters.cs` and the `otel-collector-config.yaml`
in repo root.

## Server log markers worth grepping

- `TIMING_ANCHOR` — clock-skew check on `PushVideo` (logs both override and
  OK paths).
- `Register: evicting stale stream` — author reconnect mints new `StreamId`,
  old one removed.
- `RegisterActiveStream` — successful registration (Warning level).
- `GetOrFetchRemoteVideo: caching #...` — cross-shard cache miss.
- `RequestKeyFrame: streamId=...` — server-issued PLI.
- `GetStream: first frame yielded to RpcStream session=... in {ms}` —
  subscribe latency from server to consumer (paired with the receiver-side
  first-frame log to localize post-visibility-restore stalls).
- `GetVideoRaw: #{StreamId} first decodable KF after dropping {SkipCount} non-KF chunks in {ElapsedMs}ms`
  — replay-tail diagnostic; non-zero `SkipCount` means the memoizer's
  `Replay` window was too narrow for the active simulcast tier count.
- `Stale stream watchdog: no bundles in {DurationSec}s for stream #{StreamId}, closing`
  — silence watchdog tripped (5 s × 2 by default).
- `ProcessFrames` drops (negative offset, pre-keyframe deltas) — periodic
  warnings every 3rd / 30th occurrence.

## Sender-side stats — `VideoRecordingStats`

File: `Services/Video/frame-envelopes.ts`. One mutable instance per recorder
run, threaded through every envelope, read by `Recorder.getStats()` and the
1 Hz health reporter.

Fields (current):

```
framesCaptured            framesProcessed
framesDroppedDimMismatch  framesDroppedBackpressure  framesDroppedOther
chunksEncoded             keyframesEncoded           bytesEncoded
encodeTimeMsSum           encodeTimeMsCount
lastCapturedEpoch         startedAtMs
wireFramesAdded           wireQueueDepth             wireMaxQueueDepth
rpcStreamFramesSkipped    floodGateSkipCount
wireLastAckAgeMs          isPeerConnected
previewClonesFailed
```

Surfaces in: `VideoDiagnosticsModal.razor` (Own stream),
`VideoQualityUI.RecorderHealthSnapshot`.

## Receiver-side stats — `VideoPlaybackStats`

Same file. **Session-level** — every concurrent playback pipeline contributes
to one shared instance.

```
chunksArrived             chunksDroppedAtBuffer       chunksDroppedDecoderError
framesDecoded             framesPresented             framesDroppedAtPresenter
bytesReceived
decodeTimeMsSum           decodeTimeMsCount
activeStreams             sessionStartedAtMs
```

`framesDroppedAtPresenter` is incremented by `mstgPresent` on skip-mode
drops (extra buffer over `CATCHUP_BUDGET_MS`) and on writer failures.

Per-stream `LatencySample` is also fed into `OnPlaybackHealth(snapshot)` and
into `OnPresentationLag(latencyMs)` (used for the wired-but-disabled A/V
sync — see [11](./11-buffering-and-av-sync.md)).

Surfaces in: `VideoDiagnosticsModal.razor` (Remote streams),
`VideoQualityUI.PlaybackHealthSnapshot`.

## Diagnostic UI

Two Blazor components:

- **`VideoDiagnosticsModal.razor`** — read-only stats:
  - **Own stream** (when a recorder is active): source resolution, codec,
    HW/SW accel, available codec categories, encode time, bitrate,
    keyframe count, drop counts (dim-mismatch, backpressure, flood-gate),
    encoder error history, orientation, connection / send status, quality
    target + signal + reason.
  - **Remote streams** (per active player): codec, layer active vs.
    requested, age, bitrate, latency, buffer span,
    `framesDroppedAtPresenter`, decoder queue depth, keyframe skips,
    quality reduction flag, mismatch warnings (requested vs. forwarded
    layer), session aggregate (capacity, health EMA).
- **`VideoDiagnosticsSettingsModal.razor`** — debug overrides (persisted in
  localStorage):
  - Force H.264 — clears the codec-detection cache and re-registers.
  - Cap outbound layers (recording side, 1..3).
  - Cap inbound layers (playback side, 1..3).
  - Bandwidth multiplier — scales `EstimatedCapacityBytesPerSec` to
    simulate constrained / plentiful network.

`video-diagnostics.ts` is the JS-side helper that builds the snapshots.

## URL parameters / debug flags

- `?renderBackend=mstg` or `?renderBackend=canvas` — force receiver render
  backend (otherwise `pickRenderBackend` decides based on what the player
  supplied).
- `setForceH264Only(true)` — TS helper, also wired through the
  diagnostics settings modal. Resets the codec-detection cache.
- `setVideoDebugEncoderFailInjection('h264')` — makes the real
  `VideoEncoder.configure()` fail for the category while `isConfigSupported()`
  still reports it (emulates Firefox's broken H.264, Mozilla bug 1918769).
  `'h264:worker'` fails only the worker encoder so the pre-flight probe passes
  and the runtime exclusion/re-pick path runs. Also in the settings modal
  ("Fail encoder configure"); applies on the next stream.
- `excludeDecoderCodec(codec)` — adds a codec string to the localStorage
  exclusion set; affects the next `RegisterMember` call.

`debugUI.*` console helpers (mirrors of the modal):

| Helper | Effect |
|---|---|
| `getForceH264Only()` / `setForceH264Only(bool)` | Toggle the H.264-only encoder selection |
| `getVideoDebugSettings()` | Read all video debug overrides at once |
| `setVideoDebugMaxOutboundLayerCount(n \| null)` | Cap encoder layer count |
| `setVideoDebugMaxInboundLayerCount(n \| null)` | Cap allocator layer ceiling |
| `setVideoDebugEstBandwidthMultiplier(value)` | Scale the receiver's capacity estimate |
| `setRequestedReceiveQuality(streamId, max, maxTemporal)` | Override receive quality for a specific stream |
| `enableAudioSync(true)` | Enable the A/V sync path (development instances only) |

## Tunable constants

All in `src/dotnet/Api/Constants.Video.cs` unless noted; TypeScript-derived
fields live in `src/nodejs/src/app-constants.ts` (`expandVideo`).

| Name | Default | Notes |
|---|---|---|
| `FrameRate` | 30 fps | Source-side frame rate target |
| `KeyFramePeriod` | 3 s | Wallclock keyframe interval |
| `KeyFramePeriodSize` | 90 frames | derived |
| `KeyFrameRequestCooldown` | 1 s (= `KeyFramePeriod / 3`) | Server-side PLI rate limit |
| `CodecSwitchHysteresisWindow` | 10 s | Delay before codec upgrades take effect |
| `MaxCameraStreamsPerChat` | 8 | Per-chat camera publisher cap |
| `StreamSilenceCheckInterval` | 5 s | Server silence watchdog interval |
| `StreamSilenceMaxConsecutiveZeroIntervals` | 2 | Tear-down after `interval × N` of no bundles |
| `MaxLiveDuration` | 8 h | Hard cap on a single publish session |
| `StreamExpirationDelay` | 30 s | StreamStore idle expiry |
| `ServerReplayTailDuration` | ~3.3 s (`KeyFramePeriod × 1.1`) | Memoizer per-layer retention |
| `ServerReplayTailSize` | 360 frames | Equivalent count (3 layers × 30 fps × 4 s) |
| `RpcStreamAckPeriod` (.NET, consumer leg) | 5 frames | Server → viewer ACK cadence |
| `RpcStreamAckAdvance` (.NET, consumer leg) | 16 frames | `AckPeriod × 3 + 1` |
| `senderBufferSize` (TS, publisher leg) | ~120 source moments (`ceil(keyFramePeriodSize × 4/3)`) | Sender's RpcStream ring |
| `pushPullBufferSize` (TS) | 30 source moments (= `frameRate`) | Capture↔RPC Denque capacity |
| `TargetBufferSize` | 10 frames | Receiver target buffer in frames |
| `TargetBufferSpanMs` (.NET / TS) | 333 ms | Receiver jitter buffer span |
| `BufferDurationTooLowMs` | ~111 ms | TargetBufferSpanMs / 3 |
| `BufferDurationTooHighMs` | ~500 ms | TargetBufferSpanMs × 1.5 |
| `LatencyReportInterval` | 500 ms | Receiver `latency-tap` cadence |
| `STOP_DRAIN_GRACE_MS` (sender) | 3 s | `Recorder.stop()` graceful drain |
| `STOP_DRAIN_GRACE_MS` (receiver) | 3 s | `Player.stop()` graceful drain |
| Decoder pool TTL (TS) | ~30 s | Time a parked decoder stays warm |
| Downscaler hang watchdog | 1.5 s | Per `process()` call; ≤ 4 in a row |
| Decoder hang watchdog | 2 s | Per pending-but-no-output |
| `MIN_SIMULCAST_SMALL_AXIS` (TS) | 150 px | Drop simulcast tiers below this |
| `MAX_FPS` / `MIN_FPS` (mstgPresent) | 120 / 10 | Bounds on present cadence |
| `CATCHUP_BUDGET_MS` (mstgPresent) | 4 s | Buffer overshoot beyond which presenter switches to skip mode |
| AIMD layer floor / ceiling (camera) | 1 / 3 | Sender simulcast layer count bounds |
| AIMD eval cadence | 5 s startup → 3 s settling → 5 s steady | `QcStartup/Settling/Steady` |

The receiver-side AIMD thresholds (`EncodeRatioBadAbove = 1.333`,
`EncodeRatioGoodBelow = 0.333`, `LastAckBadMs = 2000`,
`LastAckGoodMs = 500`, `SenderFrameDropRatio bad/good = 0.20 / 0.10`,
capacity backoff `0.7×`, climb cap `√2×`, cold-start `1.5 Mbps`, floor
`50 kbps`) live in `VideoQualityUI.cs` (`RecordingThresholds.Defaults`,
`PlaybackThresholds.Defaults`).

## Where to look when something is wrong

| Symptom | First place |
|---|---|
| Stream never starts on receiver | `LiveVideoStreams.GetStream` first-frame log; `LiveVideoBackend.List` returns the stream? |
| Frames drop after a few seconds | `Stale stream watchdog` log; `AppMeters.VideoSendDropRatio` |
| Receiver buffer thrash, "QualityReductionRequested" | `VideoPlaybackStats.chunksDroppedAtBuffer`, decoder queue depth |
| `framesDroppedAtPresenter` climbing | Buffer overshoot beyond `CATCHUP_BUDGET_MS = 4 s`; investigate decoder lag or sustained capture jitter |
| Layer change not honoured | `ChangePlaybackQuality` arriving? `ReceiveQualityFilter` getting fresh keyframe? PLI cooldown? |
| Wrong codec on encoder | `RegisterMember` heartbeats from all viewers? `CodecSwitchHysteresisWindow` still running? |
| Cross-pod fan-out duplicates | `RemoteVideoStreamCache` cache hit ratio (look for `caching #...` logs) — should always coalesce via `EnsureFetched` |
| Encoder hangs / NVENC errors | `AsyncVideoEncoder.handleEncoderReset` logs; HW probing fallback in `codec-support.ts` |
| Receiver stuck on black | MSTG watchdog logs; consider `?renderBackend=canvas` |
| Cold-replay drops | `GetVideoRaw: ... first decodable KF after dropping ... non-KF chunks` — replay tail too narrow |
| Downscaler hangs | "downscale: hang watchdog fired N times" — recreates each time, bails after 4 |
