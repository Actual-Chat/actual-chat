# 09 — Diagnostics, observability, and tunables

This doc collects what's measured, where it surfaces, and which constants you
might want to know about when debugging or tuning.

## Server-side meters

File: `src/dotnet/Core.Server/Diagnostics/AppMeters.cs` and
`src/dotnet/Streaming.Service/Diagnostics/StreamingMeters.cs`,
`StreamingInstruments.cs`.

| Instrument | Kind | Source of values |
|---|---|---|
| `VideoStreamCount` | UpDownCounter<int> | `StreamStore` publish/expire on each backend pod |
| `VideoLatency` | Histogram<double>, ms | (reserved; populated where end-to-end latency lands) |
| `VideoSendEncodeRatio` | Histogram<double> | `ChangeRecordingQuality(info.Health.EncodeRatioP90)` |
| `VideoSendDropRatio` | Histogram<double> | `info.Health.SenderFrameDropRatioEma` |
| `VideoSendAckAgeMs` | Histogram<double> | `info.Health.LastAckAgeMs` |
| `VideoSendLayerCount` | Histogram<int> | `state.EffectiveLayerCount` |
| `VideoReceiveCapacityBps` | Histogram<long> | `ChangePlaybackQuality(info)` |
| `VideoReceiveAggregateHealth` | Histogram<double> | byte-weighted aggregate health from `info` |
| `VideoReceiveKeyframeSkips` | Counter<long> | `info.KeyframeSkipsInWindow` |
| `VideoReceiveDecoderQueue` | Histogram<double> | `info.DecoderQueueDepthEma` |
| `VideoFrameDeserializeDuration` | Histogram<double>, µs | `CachingVideoFrameFormatter` deserialize |
| `VideoFrameSerializeDuration` | Histogram<double>, µs | same, serialize |
| `VideoFrameSizeBytes` | Histogram<int> | encoded chunk size |
| `VideoActiveConsumers` | UpDownCounter<int> | `LiveVideoStreams.GetStream` enter/exit |
| `VideoFramesReceived` / `VideoBytesReceived` | Counter<long> | publish path |
| `VideoFramesSent` / `VideoBytesSent` | Counter<long> | per-consumer fan-out |

Routing into the OTEL collector and Grafana is in
`Core.Server/Diagnostics/AppMeters.cs` and the `otel-collector-config.yaml`
in repo root.

## Server log markers worth grepping

- `TIMING_ANCHOR` — clock-skew override on `PushVideo`.
- `Register: evicting stale stream` — author reconnect mints new `StreamId`,
  old one removed.
- `GetOrFetchRemoteVideo: caching #...` — cross-shard cache miss.
- `RequestKeyFrame: streamId=...` — server-issued PLI.
- `GetStream: first frame yielded to RpcStream session=... in {ms}` —
  subscribe latency from server to consumer.
- `ProcessFrames` drops (negative offset, pre-keyframe deltas) — periodic
  warnings every 3rd / 30th occurrence.

## Sender-side stats — `VideoRecordingStats`

File: `frame-envelopes.ts`. One mutable instance per recorder run, threaded
through every envelope, read by `Recorder.getStats()` and the 1 Hz health
reporter.

Fields:

```
framesCaptured  framesProcessed  framesDroppedDimMismatch
framesDroppedBackpressure  framesDroppedOther
chunksEncoded  keyframesEncoded  bytesEncoded
encodeTimeMsSum  encodeTimeMsCount
lastCapturedEpoch  startedAtMs
wireFramesAdded  wireQueueDepth  wireMaxQueueDepth
wireFramesDropped  wireKeyframesDropped
rpcStreamFramesSkipped  wireLastAckAgeMs  isPeerConnected
```

Surface: `VideoDiagnosticsModal.razor`,
`VideoQualityUI.RecordingHealthSnapshot`.

## Receiver-side stats — `VideoPlaybackStats`

Same file. One per `PlaybackSession` (i.e. per worker, shared across all
playing streams).

Fields:

```
chunksArrived  chunksDroppedAtBuffer  chunksDroppedDecoderError
framesDecoded  framesPresented
bytesReceived  decodeTimeMsSum  decodeTimeMsCount
activeStreams  sessionStartedAtMs
```

Per-stream `LatencySample` is also fed into `OnPlaybackHealth(snapshot)` and
into `OnPresentationLag(latencyMs)` (used for A/V sync of camera+audio).

Surface: `VideoDiagnosticsModal.razor`,
`VideoQualityUI.PlaybackHealthSnapshot`.

## Diagnostic UI

Two Blazor components:

- **`VideoDiagnosticsModal.razor`** — read-only stats:
  - For the active recorder (if any): `OwnStreamDiagnostics`
    (per-layer encoder state, bitrate, encode time, keyframe count, source
    resolution, codec, HW accel, drop count, encoder error history,
    orientation).
  - For each remote stream playing: `RemoteStreamDiagnostics`
    (decoder stats, bitrate, latency, buffer span, forwarded layer id,
    observed max layer, keyframe skip count, decoder queue, quality reduction
    flag, requested vs. forwarded layer mismatch).
- **`VideoDiagnosticsSettingsModal.razor`** — debug overrides:
  - Force H.264 (localStorage).
  - Cap outbound layers (debug only).
  - Cap inbound layers (debug only).
  - Playback override per-session: Degrade / Keep / Upgrade.

`video-diagnostics.ts` is the JS-side helper that builds the snapshots.

## URL parameters / debug flags

- `?renderBackend=mstg` or `?renderBackend=canvas` — force receiver render
  backend (otherwise `pickRenderBackend()` decides).
- `setForceH264Only(true)` — TS helper, also wired through the diagnostics
  modal. Resets the codec-detection cache.
- `excludeDecoderCodec(category)` — adds a category to a localStorage
  exclusion set; affects the next `RegisterMember` call.

## Tunable constants

All in `src/dotnet/Api/Constants.Video.cs` unless noted.

| Name | Default | Notes |
|---|---|---|
| `FrameRate` | 30 fps | Source-side frame rate target |
| `KeyFramePeriod` | 3 s | Wallclock keyframe interval |
| `KeyFrameRequestCooldown` | 1 s | PLI rate limit (`KeyFramePeriod / 3`) |
| `CodecSwitchHysteresisWindow` | 10 s | Delay before codec upgrades take effect |
| `MaxCameraStreamsPerChat` | 8 | Per-chat camera publisher cap |
| `CameraFrameSilenceTimeout` | 10 s | Watchdog for camera stalls |
| `ScreenCastFrameSilenceTimeout` | 3 min | Watchdog for screencast stalls |
| `MaxLiveDuration` | 8 h | Hard cap on a single publish session |
| `StreamExpirationDelay` | 30 s | StreamStore idle expiry |
| `ServerReplayTailDuration` | ~3.3 s | Memoizer per-layer retention |
| `ServerReplayTailSize` | ~360 frames | Equivalent count |
| `RpcStreamAckPeriod` | 5 frames | ACK cadence per RpcStream |
| `RpcStreamBufferSize` | 10 frames | Outstanding-frame budget |
| `TargetBufferSize` | 10 frames | Receiver target buffer in frames |
| `TARGET_BUFFER_SPAN_MS` (TS) | 333 ms | Receiver jitter buffer span |
| `STOP_DRAIN_GRACE_MS` (sender, receiver TS) | 1 s | Graceful drain on stop |
| Encoder pool TTL (TS) | 5 s | Time a parked encoder stays warm |
| Decoder pool TTL (TS) | 30 s | Time a parked decoder stays warm |
| `MIN_SIMULCAST_SMALL_AXIS` (TS) | 150 px | Drop simulcast tiers below this |
| `keyFrameRequestCooldownMs` (TS) | 10 s | Receiver-side PLI rate limit |
| AIMD layer floor / ceiling | 1 / 3 | Sender simulcast layer count bounds |

The receiver-side AIMD thresholds (`EncodeRatioBadAbove = 1.333`,
`EncodeRatioGoodBelow = 0.333`, `BufferDurationTooLowMs ≈ 111`,
`BufferDurationTooHighMs ≈ 333`, `DecoderQueueDepthBadAbove ≈ 30`, capacity
backoff `0.7×`, climb cap `√2×`, cold-start `1.5 Mbps`, floor `50 kbps`) live
in `VideoQualityUI.cs`.

## Where to look when something is wrong

| Symptom | First place |
|---|---|
| Stream never starts on receiver | `LiveVideoStreams.GetStream` first-frame log; `LiveVideoBackend.List` returns the stream? |
| Frames drop after a few seconds | `ProcessFrames` silence watchdog logs; `AppMeters.VideoSendDropRatio` |
| Receiver buffer thrash, "QualityReductionRequested" | `VideoPlaybackStats.chunksDroppedAtBuffer`, decoder queue depth |
| Layer change not honoured | `ChangePlaybackQuality` arriving? `ReceiveQualityFilter` getting fresh keyframe? PLI cooldown? |
| Wrong codec on encoder | `RegisterMember` heartbeats from all viewers? `CodecSwitchHysteresisWindow` still running? |
| Cross-pod fan-out duplicates | `RemoteVideoStreamCache` cache hit ratio (look for `caching #...` logs) |
| Encoder hangs / NVENC errors | Encoder pool sweep logs; HW probing fallback in `codec-support.ts` |
| Receiver stuck on black | MSTG watchdog logs; consider `?renderBackend=canvas` |
