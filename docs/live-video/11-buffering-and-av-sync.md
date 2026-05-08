# 11 — Buffering goals & A/V sync

The earlier docs in this folder describe the pipeline as it actually runs.
This one covers two design intents that were stated in older planning docs
(since removed) and compares them with the current code.

The two topics are related: both are about owning the latency of a media
stream in exactly one place rather than scattering small buffers across the
pipeline.

## Part A — buffering: "one big buffer per side"

### Original goal (from older planning docs)

The plan distinguished three buffering concepts and constrained them
strictly:

> The pipeline distinguishes three concepts:
> - **`replaceable slot`**: a size-1 handoff slot before a slow component. It
>   does not intentionally add latency. It prevents the slow component from
>   going idle while waiting for the next frame. If a newer frame arrives
>   while the slot is occupied, the newer frame replaces the pending frame.
> - **`RpcStream`**: a real-time stream credit window. It may temporarily
>   hold unsent frames, but it is not a playback buffer. After ACK
>   processing, real-time stream behavior compacts unsent frames to the
>   latest decoder-safe frame when possible.
> - **`drop oldest`**: a bounded replay/storage policy. When the storage
>   exceeds its size, the oldest frames are removed first.
>
> Only `video buffer` is allowed to intentionally hold playback latency.

In other words: **one big buffer per side**. Intermediate stages use only
size-1 replace-slot inboxes; they don't measure or own latency.

Sender side parameters from the plan:

| Parameter | Value |
|---|---|
| Sender policy | real-time `RpcStream` (no extra buffer) |
| `RpcStream.BufferSize` | 10 frames |
| `RpcStream.AckPeriod` | 5 frames |
| `IsRealTime` | `true` |
| `CanSkipTo` | predicate, true on keyframes |

Receiver side parameters from the plan (the "video buffer"):

| Parameter | Value |
|---|---|
| Receiver policy | one intentional playback buffer |
| `TargetBufferSize` | 10 frames |
| `TargetBufferDuration` | 333 ms |
| `MinBufferSize` | 5 frames |
| `MaxBufferSize` | 15 frames |
| `BufferHysteresisSize` | 5 frames |
| Trim policy | keyframe-aware: above max, skip forward to a keyframe |

Receiver buffer was also expected to be **pre-decode** (encoded chunks
buffered, then decoded just-in-time), so a "skip to keyframe" trim doesn't
waste decoder work on frames that will be dropped.

### Current state and why it differs

Reality forced a small relaxation on the **sender** side and a different
buffer position on the **receiver** side.

#### Sender — push pipeline + one trailing pull-bound buffer

The encoder side of the worker is fundamentally **push-style**: the source
emits `VideoFrame`s at ~30 fps, the downscaler runs per source frame, the
encoders are submitted per layer per source frame. There is no upstream
back-pressure that can throttle the source — `getUserMedia` doesn't honour
"please slow down". So even though the operator chain is written as async
iterables, between source and the wire-send sink it is a fixed-rate
production line.

The pull side starts at the wire. `RpcStream<VideoFrameDto>` is consumed by
the API pod over WebSocket — that's where you actually get back-pressure
(disconnects, slow ACKs, peer churn). That asymmetry means there has to be
**at least one buffer between the push pipeline and the pull-bound RPC peer**;
if there weren't, a 200 ms hiccup on the wire would propagate back into the
encoder and cause it to stutter.

So today the sender has two structures, not one:

```
encode → wireSend.send()                ▲ push side, fixed rate
              │                          │ ack-driven compaction kicks in here
              ▼                          │
        Denque<VideoStreamFrame>  ◀──────┘
              │
              ▼ generator pulled by RpcStream
        RpcStream  ─────▶  WebSocket
              │  AckPeriod = 5
              │  BufferSize = 10
              │  canSkipTo = isKeyFrame
              ▼
            wire
```

The Denque is `wireSend`'s producer queue (file:
`Services/Video/operators/wire-send.ts`). It is **not unbounded** and it is
not a "drop oldest" policy:

- On every keyframe enqueue, if the queue holds **more than 2 keyframes for
  any layer**, the older frames before the most recent keyframe are dropped
  (`compactIfOverflowing`).
- Drops are counted in `VideoRecordingStats.wireFramesDropped` and
  `wireKeyframesDropped`.

`RpcStream`'s own `canSkipTo: isKeyFrame` compaction kicks in further down,
on the wire side. So the design has **two compaction stages** stacked, both
keyframe-anchored:

1. Pre-RPC Denque compaction (drops before the RPC layer ever sees them).
2. RPC ACK-driven compaction (drops after ACK rotates the credit window).

Why two? Because the RPC layer's `BufferSize = 10` is small (≈ 333 ms) and
the compaction it does is conservative — it can only collapse to the newest
decoder-safe frame within its window. The pre-RPC Denque sits in front and
gives a longer scan window for keyframe-anchored compaction. Without it, a
multi-second wire stall would let frames pile up in the unobservable RPC
ring.

In practice this means the doc's "one buffer per side" rule is **almost
honoured** on the sender:

- All upstream stages (`mstpSource`, `stampCaptureTime`, `attachSourceDims`,
  `downscale`, `applyKeyframePolicy`, `encode`) have effective queue depth
  ≤ 1; they run synchronously on each frame.
- The push→pull boundary requires the Denque-plus-RpcStream pair, and that
  pair is the only place compaction policy is configured.
- Numerically the older plan's `BufferSize = 10` / `AckPeriod = 5` are still
  the canonical numbers; whether the Denque is treated as inside or outside
  the "one buffer" depends on how strictly you read the rule.

The "ladder of compaction" — Denque keyframe-compact → RPC `canSkipTo` —
matches the spirit of the plan: encoded video is only dropped at decoder-safe
points (keyframes), and there is no speculative trimming inside the encoder
chain.

#### Receiver — one intentional buffer, post-decode pacing

The receiver is closer to the original plan but with one structural
difference: the buffer is **encoded** and **paced**, with the decoder fed
just-in-time.

```
pull              resetOnEpochChange       pacedEncodedBuffer        decode         present
(VideoFrameDto)  (no buffering, just ─▶  (intentional playback ─▶  (≤2 in    ─▶  (single
                  detects epoch flip)     buffer; 333 ms span)      flight)       slot)
```

- `EncodedFrameBuffer` (file: `playback/encoded-frame-buffer.ts`) is the
  one and only intentional latency holder. Target span is
  `TARGET_BUFFER_SPAN_MS = 333 ms` — same number as the plan's
  `TargetBufferDuration`.
- It is **encoded**: holds `ArrivedChunk`s, not `VideoFrame`s. So
  keyframe-anchored skip-forward is in principle possible here, although the
  current implementation only paces — it does not auto-trim above a "max
  span" threshold. (The plan's `MaxBufferSize = 15` / hysteresis logic isn't
  implemented; the controller in `VideoQualityUI` reacts to over-deep
  buffers indirectly via the verdict classifier.)
- The decoder has only its own internal queue (≤ 2 in flight via
  `AsyncVideoDecoder` adapter); it isn't an intentional buffer.
- Both presenters are size-1: `present-mstg.ts` uses a single replace-slot
  before the MSTG writer; `present-canvas.ts` draws each frame once.

Reading the Min/Max/Hysteresis numbers from the old plan: they map to the
current quality controller's verdict thresholds in `VideoQualityUI.cs`
(`BufferDurationTooLowMs ≈ 111`, `BufferDurationTooHighMs ≈ 333`) rather
than to in-buffer evict/skip thresholds. The action ("buffer too low ⇒
reduce quality / request keyframe") happens via `ChangePlaybackQuality`,
not inside the buffer.

So the rule "one big buffer per side" holds on the receiver:

- `pull` and `resetOnEpochChange`: no queue.
- `pacedEncodedBuffer`: the buffer.
- `decode`: small platform FIFO.
- `present`: size-1 replace.

### Summary table

| Stage | Old plan | Current |
|---|---|---|
| Sender intermediates | size-1 replace slots | same (operators are sync per frame) |
| Sender push→pull | RpcStream alone (Buf=10, Ack=5) | Denque + RpcStream pair, both keyframe-compact |
| Receiver encoded buffer | one intentional buffer, 333 ms, max 15 frames | one buffer, 333 ms, no hard cap (controller-driven) |
| Receiver post-decode | (unspecified) | size-1 replace before presenter |
| Buffer trims at | keyframes | keyframes |

The principal residual deviation from the plan is the
**Denque-plus-RpcStream pair on the sender** and the **lack of an in-buffer
high-watermark trim on the receiver** (the controller handles it instead).

## Part B — audio/video sync

### Original goal (from older planning docs)

> Every audio and video unit should carry origin capture time. The receiver
> uses that origin timeline to build a local presentation mapping for each
> author or media stream:
>
> ```
> origin media time -> local presentation time
> ```
>
> For audio paired with video from the same author, the audio target delay
> matches the video target delay so audio and video stay synchronized. The
> video pipeline establishes the shared delay because video is realtime and
> owns the A/V presentation point. Audio adopts that delay; it does not
> drive it.

The catch-up algorithm:

> The catch-up decision has two options:
> 1. **Temporary speed-up:** drop a regular pattern of audio frames, such as
>    every fourth frame, so audio plays faster while remaining intelligible.
> 2. **Hard skip:** discard the old audio region and resume at the desired
>    presentation point.
>
> The receiver chooses between these by estimating how long speed-up would
> take. If the needed correction can be absorbed within a short configured
> window, for example 3-5 seconds, it uses temporary speed-up. If the
> correction is too large, for example at or above roughly 2 seconds of
> skipped media, it hard-skips.

Constants the plan stipulated:

| Constant | Value |
|---|---|
| `PlaybackHardSkipThreshold` | 2 s |
| `PlaybackMaxSpeedUpDuration` | 5 s |
| `PlaybackSpeedUpDropEveryNFrames` | 4 |
| `AudioCatchUpDeadband` | 200 ms |
| `AudioCatchUpBaselineDelta` | -100 ms |
| `PlaybackCatchUpCommandCooldown` | 1 s |

Why audio is the one that adjusts:

- Audio recording/upload is loss-preserving — the server transcribes and
  persists everything sent. Compaction is allowed only at playback.
- Video can drop deltas between keyframes without breaking decode (and the
  whole live-video controller relies on this).
- So if A/V drift, audio is the lever — speed-up or skip — because video's
  presentation timing is anchored on its keyframe-aware buffer.

### Current state — wired but disabled

The mechanism is fully implemented in code; it just doesn't run by default.

#### The pieces (all live)

1. **Lag reporting from both sides into a shared tracker**
   - Video: `latency-tap` operator samples `frameAgeMs` every ~1 s, sends
     it to main, which calls `VideoTrackPlayer.OnPresentationLag` (camera
     streams only — screencast is excluded as too jittery). That calls
     `PlaybackLagTracker.UpdateVideo(authorId, streamId, lag)`.
     - File: `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoTrackPlayer.razor`
     - File: `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts`
     - File: `src/dotnet/UI.Blazor.App/Services/Video/operators/latency-tap.ts`
   - Audio: `AudioTrackPlayer.OnPresentationLag` calls
     `PlaybackLagTracker.UpdateAudio(authorId, _id, lag)`.
     - File: `src/dotnet/UI.Blazor.App/Components/AudioPlayer/AudioTrackPlayer.cs`

2. **Per-author pairing**
   `PlaybackLagTracker` keys EMAs by `AuthorId` and exposes
   `GetSnapshot(authorId) → { AudioLag, VideoLag }`.
   Pairing is implicit: same author publishing both audio and video ⇒
   same `AuthorId` ⇒ same snapshot.
   - File: `src/dotnet/UI.Blazor.App/Services/PlaybackLagTracker.cs`

3. **Catch-up policy**
   `LiveAudioCatchUpPolicy.GetDesiredCatchUp(authorId)`:
   ```
   snapshot = PlaybackLagTracker.GetSnapshot(authorId)
   desired  = snapshot.AudioLag - snapshot.VideoLag - AudioCatchUpBaselineDelta
   apply deadband (200 ms), clamp to ≥ 0
   ```
   The baseline delta is `-100 ms`: audio is intentionally kept slightly
   ahead of video to account for decoder/output latency on the audio side.

4. **Audio worker primitives**
   The Opus decoder worker exposes two RPC handlers on its encoded-frame
   buffer:
   - `skipUntil(ms)` — drops frames with `sourceOffsetMs < ms`.
   - `speedUpUntil(ms, dropEveryN)` — drops every Nth frame until reaching
     `ms`.
   - File: `src/dotnet/UI.Blazor.App/Components/AudioPlayer/workers/opus-decoder.ts`
   - File: `src/dotnet/UI.Blazor.App/Components/AudioPlayer/audio-player.ts`

5. **Application logic in `AudioTrackPlayer`**
   `ApplyAudioSync()` is called every 10th audio frame
   (`AudioSyncPolicySamplePeriodFrames`):
   - Reads `desired` from the policy.
   - If `desired ≥ PlaybackHardSkipThreshold (2 s)` → call
     `_playbackEngine.SkipUntil(targetMs)`.
   - Else if `desired > 0` → compute
     `speedUpDuration = min(desired × dropEveryN, PlaybackMaxSpeedUpDuration)`
     and call `_playbackEngine.SpeedUpUntil(targetMs, dropEveryN)`.
   - Cooldown of `PlaybackCatchUpCommandCooldown = 1 s` between commands.
   - File: `src/dotnet/UI.Blazor.App/Components/AudioPlayer/AudioTrackPlayer.cs:163`

#### The kill switch

`ApplyAudioSync` returns immediately if a single flag is false:

```csharp
// ChatAudioUI.cs
public bool IsAudioSyncEnabled { get; set; } = false;  // NOTE(AY): Needs testing!
```

Toggle path:

- File: `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs`
- Toggle: `DebugUI.EnableAudioSync(true|false)` from a JS console, only on
  development instances (`HostInfo.IsDevelopmentInstance`).
- File: `src/dotnet/UI.Blazor/Services/DebugUI/DebugUI.Settings.cs`

There is no end-user setting, no chat-level configuration, no automatic
enablement. The flag is wired to the only gate; flip it and the whole chain
runs.

#### Match against the plan

| Plan element | Current code |
|---|---|
| Origin time on every frame | Yes (`Offset` + `OffsetEpoch` for video, `Offset` for audio) |
| Per-author pairing | Yes (`AuthorId`-keyed) |
| Video establishes target, audio adapts | Yes (audio reads video lag, computes correction) |
| Speed-up drop pattern | Yes (`PlaybackSpeedUpDropEveryNFrames = 4`) |
| Hard-skip on large drift | Yes (`PlaybackHardSkipThreshold = 2 s`) |
| Speed-up bounded duration | Yes (`PlaybackMaxSpeedUpDuration = 5 s`) |
| Deadband / baseline | Yes (`AudioCatchUpDeadband = 200 ms`, `AudioCatchUpBaselineDelta = -100 ms`) |
| Cooldown between commands | Yes (`PlaybackCatchUpCommandCooldown = 1 s`) |
| Production-enabled | **No.** Gated by `IsAudioSyncEnabled = false` |

#### Side note on direction

There was an intermediate `AudioVideoSync` design where audio published
`playingAt` and video chased it (the inverse of the original plan).
The **current** code in this repo has the direction the plan prescribes:
video reports lag via `OnPresentationLag`, audio reads it via
`PlaybackLagTracker`, and `AudioTrackPlayer` is the actor that adjusts.
Whatever inverted scaffold existed has been removed or was never wired into
the audio side.

#### Practical implications

If you turn on `IsAudioSyncEnabled` today:

- The hooks fire on every audio playback session for any chat author who is
  also publishing video on a camera stream.
- The first `ApplyAudioSync` call within any conversation runs ~10 frames
  (≈ 200 ms) into playback — that's the sample period.
- Drift > 2 s ⇒ audio jumps forward. Drift between 200 ms and 2 s ⇒ audio
  speeds up by dropping 1 in 4 frames until aligned. Drift below 200 ms ⇒
  no action (deadband).
- It only runs for live audio; archived ("replay") audio is on a different
  player path.

The `NOTE(AY): Needs testing!` comment is the truthful summary of the
current state: structurally complete, semantically untested at scale, no UX
to enable it, and not yet exercised in production.
