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

| Parameter | Plan value |
|---|---|
| Sender policy | real-time `RpcStream` (no extra buffer) |
| `RpcStream.BufferSize` | 10 frames |
| `RpcStream.AckPeriod` | 5 frames |
| `IsRealTime` | `true` |
| `CanSkipTo` | predicate, true on keyframes |

Receiver side parameters from the plan (the "video buffer"):

| Parameter | Plan value |
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
buffer-position-and-policy on the **receiver** side.

#### Sender — push pipeline + flood-gated Denque + RpcStream ring

The encoder side of the worker is fundamentally **push-style**: the source
emits `VideoFrame`s at ~30 fps, the downscaler runs per source frame, the
encoders are submitted per layer per source frame. There is no upstream
back-pressure that can throttle the source — `getUserMedia` doesn't honour
"please slow down".

What we have instead is a single backpressure channel **at the very front**
of the pipeline (the flood gate) plus the RpcStream ring **at the very back**.
Everything in between is one frame deep.

```
Capture                                     ▲ closes when Denque ≥ size/2
  │                                          │ opens when Denque ≤ size/4
  ▼                                          │
floodGate (backpressure valve) ◀─────────────┘
  ▼
stamp / dim / downscale / KF policy / encode    (each operator is sync per frame)
  ▼
wireSend.send(VideoStreamFrameBundle)            ▲ push side
  │                                               │
  ▼                                               │
push-to-pull-buffer (Denque<VideoStreamFrameBundle>, capacity = pushPullBufferSize ≈ 30 ≈ 1 s)
  │
  ▼ generator pulled by RpcStream
RpcStream<VideoFrameBundleDto>  ─────▶  WebSocket
  │  ackPeriod ≈ 3, ackAdvance = 10
  │  bufferSize ≈ keyFramePeriodSize × 4/3 ≈ 120 source moments
  │  canSkipTo: bundle ⇒ Layers[0].IsKeyFrame
  ▼
wire
```

So today the sender has three structures, not one:

1. **The flood gate** — a hysteresis valve right after capture that drops
   captured frames before they hit the encoder/downscaler. Effectively a
   resource-saving "don't even try" path; it's not a buffer because it
   holds nothing.
2. **The Denque in `push-to-pull-buffer.ts`** — a small (~1 s) FIFO between
   the synchronous wireSend output and the asynchronous RpcStream pump.
   Its job is to be there at all: without it, an RPC stall would block
   `wireSend.send(...)` itself, which is on the encode pipeline's hot path.
3. **The RpcStream sender ring** — the actual on-the-wire credit window.
   `bufferSize ≈ 120 source moments` is large by design: under transient
   wire stalls, `canSkipTo: isKeyFrame` compacts older non-keyframe bundles
   inside the ring rather than letting the producer block.

Compaction therefore happens at **two stacked stages**, both
keyframe-anchored:

1. The Denque holds at most ~1 s of bundles; the flood gate engages well
   before that and stops feeding more source frames in.
2. RPC ACK-driven compaction inside the ring drops older non-KF bundles
   when the wire is slow.

Why two? Because the flood gate at ½-capacity is the cheapest place to
absorb the load, and the RPC ring's own compaction handles the residual
in-flight stall. Without the front-end gate, the Denque would cap out and
the encoder would back up; without the ring's compaction, a brief wire
stall would propagate as decoder gaps on the consumer side.

In practice this means the doc's "one buffer per side" rule is **almost
honoured** on the sender:

- All upstream operators have effective queue depth ≤ 1; they run
  synchronously per frame.
- The push→pull boundary requires the Denque-plus-RpcStream pair, and that
  pair is the only place latency is held — but the **flood gate** is what
  enforces an upper bound, so there's no policy decision in the Denque
  itself ("drop oldest" never fires there in practice).
- The numbers are different from the plan because the bundle-per-source-moment
  shape changes the units: `bufferSize ≈ 120 source moments` is roughly the
  same number of *encoded chunks* as the plan's `BufferSize = 10` would be
  per-layer (~3 layers × 30 fps × 1.33 ≈ 120 frames); `ackPeriod` derives
  from `targetBufferSize` instead of being hard-coded.

#### Receiver — one intentional buffer, span-gated, post-pull

The receiver is closer to the original plan but with structural differences
in **what** is buffered and **how** the gate fires.

```
pull              resetOnEpochChange       pacedEncodedBuffer        decode         present
(VideoFrameDto)  (no buffering, just ─▶  (intentional playback ─▶  (≤2 in    ─▶  (single
                  detects epoch flip)     buffer; 333 ms span)      flight)       slot, paced)
```

- `EncodedFrameBuffer` (file: `playback/encoded-frame-buffer.ts`) is the
  one and only intentional latency holder. Target span is
  `Constants.Video.TargetBufferSpanMs ≈ 333 ms` — same number as the plan's
  `TargetBufferDuration`.
- It is **encoded**: holds `ArrivedChunk`s, not `VideoFrame`s.
- Gate is **span-based, not count-based**: `tryPull()` only releases a
  chunk while `spanMs() ≥ targetSpanMs`. `spanMs` is anchored on
  `capturedAt` so it tracks source pacing rather than wallclock arrivals
  (an earlier capture-time-anchor design self-corrected drift poorly;
  span-gating self-corrects on every push/pull).
- The decoder has only its own internal queue (≤ 2 in flight via
  `AsyncVideoDecoder` adapter); it isn't an intentional buffer.
- The presenter (`mstgPresent`) does its own pacing on top of the buffer
  span: 60-fps cap, capture-time-delta scheduling for steady state, and a
  skip mode when the buffer is more than `CATCHUP_BUDGET_MS = 4 s` over
  target. The skip path drops frames at the presenter and counts them in
  `framesDroppedAtPresenter`.

The plan's `Min/Max/Hysteresis` numbers map to the current quality
controller's verdict thresholds in `VideoQualityUI.cs`:
`BufferDurationTooLowMs ≈ 111` (= `TargetBufferSpanMs / 3`),
`BufferDurationTooHighMs ≈ 500` (= `TargetBufferSpanMs × 1.5`). The action
("buffer too low ⇒ reduce quality / request keyframe") happens via
`ChangePlaybackQuality`, not inside the buffer.

So the rule "one big buffer per side" holds on the receiver:

- `pull` and `resetOnEpochChange`: no queue.
- `pacedEncodedBuffer`: the buffer.
- `decode`: small platform FIFO.
- `present`: own pacing + skip mode, but no queue beyond the writer.

### Summary table

| Stage | Old plan | Current |
|---|---|---|
| Sender intermediates | size-1 replace slots | same (operators are sync per frame); flood gate is a resource gate, not a buffer |
| Sender push→pull | RpcStream alone (Buf=10, Ack=5) | Denque (~1 s) + RpcStream pair (~120 source-moment ring); both keyframe-compact |
| Receiver encoded buffer | one intentional buffer, 333 ms, max 15 frames | one buffer, 333 ms, **span-gated** (no hard count cap; controller-driven backoff via `ChangePlaybackQuality`) |
| Receiver post-decode | (unspecified) | own pacing in `mstgPresent` (60 fps cap, 4 s catch-up budget, skip mode) |
| Buffer trims at | keyframes | keyframes (upstream RPC ring) + capture-time pacing (downstream presenter) |

The principal residual deviation from the plan is the
**Denque-plus-RpcStream pair on the sender** with a flood gate front-stop,
and the **lack of an in-buffer high-watermark trim on the receiver** (the
controller plus the presenter's skip mode handle it instead).

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

### Current state — wired and enabled; audio-master under video skip

The mechanism is fully implemented and **enabled by default**
(`IsAudioSyncEnabled = true`). Beyond the original "audio adapts to video" catch-up,
the receiver now also handles the **video-skip** case explicitly: under poor network
the video buffer skips to the **audio capture-point** (`skip-to-audio`) instead of
the live edge, so video lands lip-synced to audio rather than racing ahead and
forcing endless audio speed-ups. While video is actively skipping, the catch-up
policy suppresses corrections (`AudioSyncSkipReason.VideoSkipping`) — audio is the
master timeline during degradation. See the audio-latency registry
(`Services/Video/audio-latency-registry.ts`) and `EncodedFrameBuffer.setAudioCaptureOffsetMs`.

#### The pieces (all live)

1. **Lag reporting from both sides into a shared tracker**
   - Video: `latency-tap` operator samples `frameAgeMs` every
     `LatencyReportInterval = 500 ms`, sends it to main, which calls
     `VideoTrackPlayer.OnPresentationLag` (camera streams only —
     screencast is excluded as too jittery). That calls
     `PlaybackLagTracker.UpdateVideo(authorId, streamId, lag)`.
     - File: `src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoTrackPlayer.razor`
     - File: `src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts`
     - File: `src/dotnet/UI.Blazor.App/Services/Video/operators/latency-tap.ts`
   - Audio: `AudioTrackPlayer.OnPresentationLag` calls
     `PlaybackLagTracker.UpdateAudio(authorId, _id, lag)`.

2. **Per-author pairing**
   `PlaybackLagTracker` keys EMAs by `AuthorId` and exposes
   `GetSnapshot(authorId) → { AudioLag, VideoLag }`.
   Pairing is implicit: same author publishing both audio and video ⇒
   same `AuthorId` ⇒ same snapshot.
   - File: `src/dotnet/UI.Blazor.App/Services/PlaybackLagTracker.cs`

3. **Hold policy**
   `AudioTrackPlayer.AdjustBufferHold(authorId)`:
   ```
   hold = clamp(videoLag + AudioCatchUpBaselineDelta,
                PlaybackTargetBufferSizeWithVideo,
                PlaybackTargetBufferSizeWithVideo + AudioSyncMaxHold)
   ```
   `AudioCatchUpBaselineDelta` is `0`: it was `-100 ms` to compensate unmeasured
   audio output latency, but the audio lag metric already includes it, so a
   negative baseline made audio genuinely lead video.

4. **Application logic in `AudioTrackPlayer`**
   `ApplyAudioSync()` runs every 10th audio frame and calls `AdjustBufferHold`,
   which moves `TargetBufferSize` with 50 ms hysteresis and a 500 ms rate limit.
   **No audio frame is ever dropped or sped up** — the hold is additive delay,
   and video paces to the audio position instead (`skip-to-audio`).
   `SetTargetBufferSize` is implemented on the web engine only, so the runtime
   half of this is web-only; every engine honours `TrackInfo.TargetBufferSize` at
   `Play()`.

#### The kill switch

`ApplyAudioSync` returns immediately if a single flag is false:

```csharp
// ChatAudioUI.cs
public bool IsAudioSyncEnabled { get; set; } = true;
```

The JS skip-to-audio behavior has its own independent kill switch
(`setSkipToAudioEnabled(false)` in `audio-latency-registry.ts`); when off it falls
back to the original skip-to-live.

Toggle path:

- File: `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs`
- Toggle: `debugUI.enableAudioSync(true|false)` from the JS console, only
  on development instances.

There is no end-user setting, no chat-level configuration, no automatic
enablement. The flag is wired to the only gate; flip it and the whole
chain runs.

#### Match against the plan

| Plan element | Current code |
|---|---|
| Origin time on every frame | Yes (`Offset` + `OffsetEpoch` for video, `Offset` for audio) |
| Per-author pairing | Yes (`AuthorId`-keyed) |
| Video establishes target, audio adapts | Yes (audio reads video lag, computes correction) |
| Speed-up drop pattern | **No** — dropped; audio always plays at 1x |
| Hard-skip on large drift | **No** — the hold is the only lever |
| Bounded correction | Yes (`AudioSyncMaxHold = 1 s` over the base buffer) |
| Deadband / baseline | Yes (50 ms hysteresis, `AudioCatchUpBaselineDelta = 0`) |
| Rate limit between adjustments | Yes (500 ms) |
| Production-enabled | **Yes.** `IsAudioSyncEnabled = true` (default) |

#### Practical implications

- The hook fires on every audio playback session for any chat author who is also
  publishing video on a camera stream, and never on your own stream.
- The first `ApplyAudioSync` call runs ~10 frames (≈ 200 ms) into playback.
- A hold change lands at the next track start — raising a buffer target cannot
  retroactively insert delay into audio already playing. Since utterances are
  VAD-segmented, per-utterance is the real granularity of this correction.
- It only runs for live audio; archived ("replay") audio is on a different
  player path.

Known gap: `AdjustBufferHold` sets `hold = videoLag`, which is open-loop.
`videoLag` is path latency plus video's own buffer, and audio's path is
approximately the same, so the hold double-counts it and biases audio late by
roughly one path latency. The closed-loop form corrects on `videoLag − audioLag`,
which cancels both path latency and clock error since both lags are measured
against the same client `ServerClock`.
