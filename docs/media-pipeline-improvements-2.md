# Media Pipeline Improvements — Part 2

Architectural recommendations for the audio/video pipeline, continued from the
in-conversation analysis. Items below were originally numbered 6–10; renumbered
A–E here.

---

## A. Platform playback engines — three near-clones

`AndroidAudioPlaybackEngine.cs`, `IosAudioPlaybackEngine.cs`,
`WindowsAudioPlaybackEngine.cs` repeat:

- `_frames: DurationTargetingFrameBuffer<AudioFrame>` field
- `WatchBufferEscalationState(chatId, …)` subscription loop
- `Constants.Audio.GetDecoderTargetBufferDuration(escalation)` translation
- catch-up wiring

**Proposal — `MauiAudioPlaybackEngineBase`** in `App.Maui/Audio/` holding the
three duplicated members; subclasses override only the native sink (AAudioTrack
vs AVAudioEngine vs WASAPI). This is a low-risk, mechanical extraction. The
same idea applies symmetrically once Maui video engines exist.

---

## B. Buffer escalation — one observable, not three subscribers ★

`AndroidAudioPlaybackEngine.cs:150`, `IosAudioPlaybackEngine.cs:146`,
`WindowsAudioPlaybackEngine.cs:191`, `audio-player.ts:150,159`,
`feeder-audio-worklet-processor.ts:268-274` each independently subscribe and
translate the same `escalation > 0` flag. The translation `0/120ms` lives in
`Constants.Audio.GetDecoderTargetBufferDuration`.

Make `ChatAudioUI.GetPlaybackBufferEscalation` return a
`IComputed<DecoderBufferProfile>` already containing the target duration;
engines just call `_frames.SetTargetDuration(profile.TargetDuration)`. Removes
the `escalation → duration` translation from five sites.

---

## C. Worker RPC scaffolding — lift the boilerplate

Audio (encoder, decoder, VAD, segmentation-style worklet) and video (encoder,
decoder, segmentation) workers each define a near-identical
`*-worker-contract.ts` + `rpcClient/Server/ClientServer` plumbing +
`MessageChannel` wiring. Worth extracting:

- `defineWorkerContract<TIn, TOut>(name)` helper that bundles contract +
  transferables list
- `bootWorker<TContract>(name, impl)` for the worker side
- `connectWorker<TContract>(url, name)` for the main side

Goes under `src/nodejs/src/rpc/` (the shared `actuallab-rpc` module per
CLAUDE.md guidance). Reduces ~6 worker-init blocks to one-liners; contracts
become the only handwritten part.

---

## D. Lag tracking — single cadence, single tracker

`PlaybackLagTracker` is media-agnostic but reporting cadence is not: video
`reportLatencyTick` ~2s vs audio `presentationLag` per-buffer-state-change.

**Proposal:** the tracker exposes `Update(media, authorId, streamId, lag)` and
itself rate-limits to a single configurable cadence (default 500ms). Each
player just calls Update unconditionally. Removes per-player throttling logic
and gives the A/V sync deterministic update intervals.

---

## E. Smaller cross-cutting wins

- **Unify RPC stream config.** Replace `Constants.Audio.StreamAckPeriod=64` /
  `StreamBufferSize=192` and `Constants.Video.RpcStreamAckPeriod=5` /
  `RpcStreamBufferSize=10` with a `RpcStreamProfile` record (`Realtime`,
  `Recording`) chosen at the call site. Audio's value is legacy and visibly
  out of step with the doc-target (5/5).
- **Single buffer-health enum.** Audio: `BufferState = 'low' | 'ok'`; video:
  `lastReportedBufferLow: boolean`. Promote to
  `BufferHealth { Low, Ok, Overrun }` in the shared TS module.
- **Constants split.** `Constants.Audio.cs` and `Constants.Video.cs` both
  define `LatencyReportInterval`, target buffer durations, stale thresholds.
  Pull the shared ones into `Constants.Streaming.cs`; keep only media-specific
  tuning in the per-media files.
- **Stream-skip math.** `LiveStreamMuxer.cs:137-138` computes
  `skipTo = (lag - StaleAudioTrimWindow).Positive()`; the same shape recurs in
  video skip-to-live. Express as `StreamOffset.SkipForLag(currentLag, window)`
  extension on a unified `StreamOffset` type.
- **Test clocks.** `DurationTargetingFrameBufferTest.cs` rolls its own clock;
  expose a small `MomentClock` test helper in `Testing.Streaming` and reuse
  for the (currently missing) video buffer tests.
