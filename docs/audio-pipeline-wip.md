# Audio Pipeline Refactoring Plan

This document tracks the incremental refactoring of the live audio pipeline
toward the target design.

## Reference docs

- [audio-pipeline.md](audio-pipeline.md) — target high-level design.
  Conceptual stages, buffering and skipping policies, and the canonical
  `Constants.Audio` block.
- [audio-pipeline-now.md](audio-pipeline-now.md) — current-state map.
  For each conceptual stage, names the matching files and classes today,
  describes how they work, and calls out the major differences from the
  target. The "Hardest Refactorings" section near the end ranks what is
  expected to be the most expensive work.

## Completed steps

### Step 1 — Centralized audio pipeline constants

Established a single source of truth for audio pipeline constants in .NET
and propagated them to TS via the same JS-interop pipe used by the video
constants. (Step 3 below completed this by adopting the doc-target named
constants on top of the structural pipe established here.)

- `Constants.Audio` (`src/dotnet/Api/Constants.Audio.cs`) was extracted
  from the omnibus `Constants.cs` into its own file, alongside
  `MaxBeginsAtDrift`. Holds server-side and shared values
  (`OpusFrameDurationMs`, `RecordingSampleRate`, `PlaybackSampleRate`,
  `StreamAckPeriod`, `StreamBufferSize`, `MaxStreamDuration`, etc.).
- `AppConstants.AudioConstants` (`src/dotnet/Api/AppConstants.Audio.cs`)
  is the JS-interop snapshot, organized into nested `Rec / Play / Encode /
  Stream / Vad` groups (and a `Heartbeat` sub-group under `Rec`).
  Registered as a singleton in `ApiModule`.
- `BrowserInit` carries `AppConstants` to the main thread; each audio
  worker / worklet receives them via an `init(appConstants)` RPC and
  populates a module-local `AUDIO` field on first call (first-call wins).
- The shared module is `src/nodejs/src/app-constants.ts`. TS consumers
  read `AUDIO.play.startBufferDuration`, `AUDIO.stream.maxBufferedFrames`,
  `AUDIO.encode.maxBufferedFrames`, `AUDIO.vad.minSpeechMs`, etc.
  Reading before init throws — intended fail-loud behavior.
- The legacy `src/nodejs/src/_constants.ts` was deleted; every TS consumer
  now goes through `app-constants`.

Outcome: the pipe for shared audio constants exists end-to-end, so any
follow-up step that introduces a target value can land it in one .NET
file and reach every consumer.

### Step 2 — A/V sync disabled by default (cross-pipeline)

Not strictly an audio-side refactor, but it removes a structural coupling
that the audio pipeline target rejects (audio publishing its `playingAt`
and video chasing it).

- `AudioVideoSync.isEnabled` defaults to `false`; reads/writes are gated
  on a `localStorage` flag toggleable from the video diagnostics modal.
  When disabled, `AudioVideoSync.update()` is a no-op and
  `AudioVideoSync.get(authorId)` returns nothing. Source:
  `src/nodejs/src/audio-video-sync.ts`.
- `AudioTrackPlayer` (`src/dotnet/UI.Blazor.App/Components/AudioPlayer/`)
  still calls `AudioVideoSync.update(...)` on each playback tick, but the
  call is now a no-op for normal users. The audio side is therefore not
  doing any presentation work it would not do in a target-aligned world,
  even though the publishing API has not been removed yet.

Outcome: in the default configuration today, audio playback is no longer
the timing source for video. This makes the target's "video establishes
the shared delay; audio adopts it" model the only cross-pipeline behavior
we need to design for, when A/V sync work is taken on later.

---

That is the full set of audio-pipeline-aligned changes since
[audio-pipeline-now.md](audio-pipeline-now.md) was written. Other recent
audio-tagged commits — heartbeat / auto-stop logic, MessagePack
serialization, AOT fixes, iOS PlayerNode race fixes,
`active-recording-svg` visibility gating — improve the existing pipeline
but do not move it toward the target design.

### Step 3 — Adopt the target `Constants.Audio` block

Brought the .NET source-of-truth in line with the doc's `Constants.Audio`
block. No behavior change — purely additive, plus aliasing to keep
existing call sites working.

- `Constants.Audio` (`src/dotnet/Api/Constants.Audio.cs`) now defines the
  doc-target names: `FrameRate = 50`, `FrameDuration` /
  `FrameDurationMs` (canonical), `StartBufferSize = 5`,
  `StartBufferDuration = 100 ms`, `BufferHysteresisSize = 3`,
  `MinBufferSize = 2`, `VoiceStartPreRollSize = 10`,
  `DeliveryRpcStreamAckPeriod = 5`, `RecordingRpcStreamAckPeriod = 5`,
  `PlaybackHardSkipThreshold = 2 s`,
  `PlaybackMaxSpeedUpDuration = 5 s`,
  `PlaybackSpeedUpDropEveryNFrames = 4`.
- `OpusFrameDuration` / `OpusFrameDurationMs` are kept as aliases of
  `FrameDuration` / `FrameDurationMs` so the 20+ existing call sites
  don't need to be touched in this step.
- The legacy combined `StreamAckPeriod = 64` / `StreamBufferSize = 192`
  remain in place and are explicitly marked as legacy in comments. They
  still drive the server→client delivery RPC streams; the recording vs.
  delivery split will replace them in a follow-up.
- `StartPlaybackWhenBufferedDuration` now points at `StartBufferDuration`
  (same 100 ms value) so MAUI's Windows playback engine and the rest of
  the codebase agree on a single canonical value.
- `AppConstants.AudioConstants` (`src/dotnet/Api/AppConstants.Audio.cs`)
  exposes the new fields to TS: top-level `FrameRate`; in `Play`,
  `StartBufferSize`, `BufferHysteresisSize`, `MinBufferSize`,
  `PlaybackHardSkipThresholdMs`, `PlaybackMaxSpeedUpDurationMs`,
  `PlaybackSpeedUpDropEveryNFrames`; in `Encode`, `VoiceStartPreRollSize`.
- `app-constants.ts` (`src/nodejs/src/app-constants.ts`) mirrors the
  schema, derives `frameDurationMs = 1000 / frameRate`, and exposes
  seconds aliases (`playbackHardSkipThreshold`,
  `playbackMaxSpeedUpDuration`) where consumers prefer seconds.
- No TS call site changes in this step. Existing reads of
  `AUDIO.play.startBufferDurationMs` and friends keep working
  unchanged; the new fields are visible but unused for now.

Outcome: every later audio-buffer step (preserving `frame.Offset`
end-to-end, moving the playback buffer pre-decode, splitting the
recording vs. delivery RPC stream parameters, removing server-side live
catch-up) can change one constant's value or read a target-named field
without re-touching the constants pipe.

### Step 4 — Drop derived fields from the .NET→TS wire format (cross-pipeline)

Tightened the JS-interop snapshot for both audio and video so that
values which are mathematical functions of inputs are computed once in
`expandAudio` / `expandVideo` rather than shipped redundantly. Every
remaining base field on `AppConstants.{Audio,Video}Constants` is an
essential value; everything else is derived TS-side.

- `AppConstants.Audio.cs`: dropped `Encode.FrameDurationMs`,
  `Play.StartBufferDurationMs`, `Play.MinBufferSize`. They are now
  computed in `expandAudio` from `audio.frameRate`,
  `play.startBufferSize`, and `play.bufferHysteresisSize`.
- `AppConstants.Video.cs`: dropped `RpcStreamAckPeriod` and
  `RpcStreamBufferSize`. They are now computed in `expandVideo` from
  `targetBufferSize` (`bufferHysteresisSize` and `targetBufferSize`
  respectively).
- `Constants.Video.cs`: tightened the C# side too —
  `KeyFramePeriodSize` and `ServerReplayTailSize` derive from
  `KeyFramePeriod` / `ServerReplayTailDuration` (× `FrameRate`)
  instead of re-encoding "3" and the implicit "× 1". This makes the
  duration the only place to change a keyframe cadence or replay-tail
  length. `KeyFramePeriodSize` changed from `const` to
  `static readonly`; it is unused in C# today, so no consumer broke.
- `app-constants.ts`: moved the dropped fields from the "From .NET"
  section to the "Derived in TS" section of `AudioConstants` /
  `VideoConstants`, and added a new top-level
  `audio.frameDurationTicks` (= `frameDurationMs × 10_000`, .NET ticks
  per audio frame).
- `audio-streamer.ts`: replaced the lazy module-level
  `FRAME_DURATION_TICKS` holder with direct reads of
  `AUDIO.frameDurationTicks`. Also swapped `AUDIO.encode.frameDurationMs`
  → `AUDIO.frameDurationMs` (the canonical top-level cadence).

Outcome: the wire format ships only essential inputs. Adding a new
derived value going forward is a single-line addition in
`expandAudio` / `expandVideo`, never a .NET schema change.

## Next steps (proposed)

After Steps 3–4, the natural next buffer-focused steps — in the order
that keeps each commit small and reviewable — are:

1. **Preserve `frame.Offset` end-to-end through the demuxer.** Today
   `LiveStreamDemuxer` writes only `frame.Data` to its per-stream
   channels and `ChatListener` reconstructs offsets by index. Carry the
   server-supplied `Offset` through, drop the index-based reconstruction
   in `ChatListener.CreateAudioSource`. Prepares the ground for a single
   client-side skip decision.
2. **Move the playback buffer pre-decode**, mirroring video Step E.1.
   Introduce an encoded-frame `audio buffer` component that owns the
   start threshold, hysteresis, starvation handling, and (later)
   speed-up / hard-skip; collapse the feeder worklet to the doc's
   small smoothing buffer (~100 ms of decoded PCM, no skip semantics).
   With Offset preserved end-to-end this buffer can start with the
   audio-only policy (keep all received audio) and pick up the
   video-aligned policy in a later commit.
3. **Remove server-side live catch-up.** Drop `LiveStreamMuxer`'s
   `MaxCatchUpLag = 3 s` skip and `AudioStreamingBackend.SkipTo`'s
   live-catch-up role for audio. Once the client buffer owns the
   decision, the server stops second-guessing it.
