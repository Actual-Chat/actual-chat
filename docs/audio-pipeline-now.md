# Audio Pipeline — Current State vs. Target

This document maps every conceptual stage from `docs/audio-pipeline.md` to the
matching piece (or pieces) of the current implementation, briefly describes how
it works today, and calls out the major differences from the target design.
Sections at the end cover the cross-cutting concerns (Time Model, Stream
Lifecycle, Startup) and the hardest expected refactorings.

All file paths are relative to the repo root.

---

## 1. `raw audio source`

**Now:**
- Web: `getUserMedia` is requested in
  `src/dotnet/UI.Blazor.App/Components/AudioRecorder/opus-media-recorder.ts`
  with mono, AEC and noise suppression on, AGC on (off on Android), 16 kHz
  preferred. The `MediaStream` feeds two `AudioWorkletNode`s in parallel — one
  for the encoder, one for VAD — both connected to the same `AudioContext`.
- MAUI capture lives under `src/dotnet/App.Maui/Platforms/{Android,Windows,iOS}/Audio/`:
  - `AndroidAudioCapture.cs` uses `AudioRecord` and writes mono 16 kHz
    `PCM_FLOAT` into a `BlockRingBuffer<float>` sized for ~10 s.
  - `WindowsAudioCapture.cs` uses `AudioGraph`, runs WebRTC APM (AEC/NS/AGC/HPF),
    and routes microphone, loopback, and processed-output through three ~10 s
    ring buffers. Adds a 40 ms mic delay (`MicDelaySamples = 16000 * 40 / 1000`)
    to align with loopback for echo cancellation.
  - `IosAudioCapture.cs` uses `AVAudioEngine` with voice processing enabled and
    resamples into a ~10 s ring buffer.
- All MAUI captures yield 20 ms PCM frames asynchronously and may drop samples
  silently if the ring fills.

**vs. doc:**
- Doc treats this stage as "no buffering, immediately pass to next." Web
  matches — the worklet drains `AudioContext` render quanta as they arrive.
- MAUI does **not** match. Each platform inserts a 10-second platform ring
  buffer between capture and the rest of the pipeline. That buffer exists for
  schedule-jitter reasons but it is far above the doc's "no intermediate
  buffer." Worse, when full it drops oldest samples silently rather than
  surfacing pressure.

## 2. `raw audio processors`

**Now:**
- Web encoder worklet:
  `src/dotnet/UI.Blazor.App/Components/AudioRecorder/worklets/opus-encoder-worklet-processor.ts`
  uses `AudioRingBuffer(8192, 1)` to assemble the ~128-sample render quanta
  into 20 ms frames (`samplesPerWindow`). Throws on overrun rather than
  silently dropping.
- Web VAD worklet + worker
  (`worklets/audio-vad-worklet-processor.ts`,
  `workers/audio-vad-worker.ts`): batches 32 ms windows; runs WebRTC VAD
  initially, swaps to neural VAD after model load. The neural-VAD swap
  **clears any queued VAD input** — an explicit data discard point that
  affects detection timing only.
- Pre-roll trimming on voice start happens inside
  `workers/opus-encoder-worker.ts` (~lines 290-310): before encoding starts,
  the head of the queued PCM is analyzed by gain and low-gain leading frames
  are dropped.
- MAUI: `MauiRecorderEngine.AudioStreamProcessor` keeps a 2 s VAD buffer and
  a 500 ms encoding pre-roll buffer. While voice is inactive, oldest 20 ms
  pre-roll frames are evicted to keep the buffer bounded. VAD runs in
  batches of `3 × 32 ms ≈ 96 ms`. On VAD start the pre-roll is trimmed
  again by gain analysis.
- WebRTC APM (AEC, NS, AGC, HPF) runs inside `WindowsAudioCapture`; on
  Android/iOS it relies on platform APM. The browser side relies on the
  browser's built-in AEC/NS via `getUserMedia` constraints.

**vs. doc:**
- The doc's pre-roll is a single bounded `drop oldest` buffer at this stage.
  Both web and MAUI roughly match this shape, though sizes differ (web's
  encoder PCM queue plus a separate VAD queue; MAUI's 2 s VAD buffer is
  larger than `VoiceStartPreRollSize = 200 ms`).
- The doc says all other processors operate per-block without queuing.
  `WindowsAudioCapture` adds a 40 ms intentional mic delay for AEC alignment
  — that is an inherent cost of host-side AEC and effectively becomes
  capture-side latency the rest of the pipeline cannot recover.

## 3. `audio encoder`

**Now:**
- Web: `workers/opus-encoder-worker.ts` keeps a `Denque<ArrayBuffer>` of
  incoming PCM frames. On each `encode` call it dequeues and encodes either
  through the system `AudioEncoder` or the WASM Opus encoder.
- The PCM queue is bounded by `AE.MAX_BUFFERED_FRAMES = 50` (~1 s). When
  exceeded, oldest PCM frames are dropped (`while (queue.length > MAX) queue.shift()`).
- Encoder pre-skip is either reported by the system encoder or set to
  `AE.DEFAULT_PRE_SKIP = 312` samples for the WASM encoder; this value is
  later forwarded as the `preSkip` parameter of `PushAudio`.
- MAUI: `IAudioCodec` wraps Opus; encoding is synchronous per frame. No
  queue.

**vs. doc:**
- The doc prescribes a short lossless handoff at this stage; current web code
  uses a `Denque` capped at 50 with drop-oldest semantics. In normal operation
  the encoder is fast enough that the queue stays near-empty. Under CPU
  pressure, however, dropping oldest samples is still a recording-side data
  loss point. The updated target forbids intentional speech drops before the
  server because the same audio is used for transcription and persistence.
- MAUI is closer in spirit (no queue), though the upstream platform ring
  buffers can hide several seconds of backlog.

## 4. `audio sender`

**Now:**
- `workers/audio-streamer.ts` → `class AudioStream`. Encoded packets are
  pushed into a `Denque<Uint8Array>` (`this.frames`). On overflow the
  oldest frame is dropped: `while (frames.length > AS.MAX_BUFFERED_FRAMES)`,
  where `MAX_BUFFERED_FRAMES = 1500` (~30 s).
- Before sending starts, the sender waits for `frames.length > AS.DELAY_FRAMES = 3`
  (~60 ms) — an intentional startup buffer.
- The sender constructs an `RpcStream<AudioFrameDto>` whose generator yields
  `{ Data, Offset = frameIndex * 20ms, Duration = 20ms, IsKeyFrame: true }`
  and calls `streamServer.PushAudio(session, chatId, repliedChatEntryId,
  clientStartOffset, preSkip, stream.toRef(peer))`.
- The `RpcStream` is constructed with **no real-time options** (no
  `isRealTime: true`, no `canSkipTo`, no explicit `bufferSize` /
  `ackPeriod`). Stream behavior falls back to default RPC stream semantics.
- On peer-change, the in-flight `PushAudio` is aborted (frames already handed
  to the failed stream are lost — `PushAudio` allows reconnect but not
  resend). The retry loop creates a brand-new `PushAudio` over whatever
  frames remain in the producer queue, with `frameIndex` reset to 0 (i.e. a
  new server-side chat entry).
- MAUI sender uses `Channel<IMemoryOwner<byte>>` bounded at
  `Constants.Audio.StreamingChannelCapacity = 1024` (~20 s). It
  backpressures while active and drains-then-discards on completion.

**vs. doc:**
- The non-realtime `RpcStream` direction is conceptually correct for
  recording upload: sender-to-server audio should not be compacted or skipped,
  because the server transcribes and persists it.
- The major mismatch is the producer-side `Denque` with `MAX_BUFFERED_FRAMES =
  1500` and drop-oldest overflow. If the sender ever falls behind for long
  enough, audio can be deleted before the server sees it. That is acceptable
  for realtime playback, but not for recording upload.
- The web sender also has a smaller PCM queue earlier in the encoder worker
  that can drop oldest PCM frames before encoding. That is another
  transcription-visible loss point.
- The target still wants short ACK periods and aggressive draining, but not
  realtime ACK compaction on the upload stream.
- The 60 ms startup delay (`DELAY_FRAMES = 3`) is essentially the doc's
  per-stream startup fill, but it is implemented as "wait for N frames in
  the producer queue" rather than as a property of the RPC stream itself.
- The "send unsent as a new stream on peer-change" contract leaks transport
  failure into recording/chat-entry semantics. A peer-change can split one
  utterance into multiple server-side entries.

## 5. `server audio receiver`

**Now:**
- `src/dotnet/Streaming.Service/Services/StreamServer.cs` →
  `PushAudio(Session, chatId, repliedChatEntryId, clientStartOffset, preSkip,
  RpcStream<AudioFrame> frameStream, ct)` parses chat/reply IDs, mints a
  fresh `StreamId` on the local node, builds an `AudioRecord`, wraps the
  inbound RPC stream, and calls `AudioStreamingBackend.ProcessAudio(...)`.
- `src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs`
  performs:
  - Frame silence watchdog (`Constants.Audio.FrameSilenceTimeout = 2 s`).
  - Permission/author checks.
  - `clientStartOffset → BeginsAt` mapping; if drift exceeds
    `Constants.Audio.MaxBeginsAtDrift = 5 s`, falls back to server time.
  - Builds an `AudioSource` that prepends an Opus header frame at offset
    `-1 ms`.
  - Publishes the frame stream to `_audioStreams.Publish(streamId, source)`.
  - Optionally registers in `LiveAudioBackend`, kicks off transcription, and
    eventually creates a chat entry and saves the audio blob.
- RPC ACK tuning is set on the producer side via `Constants.Audio.StreamAckPeriod = 64`
  and `Constants.Audio.StreamBufferSize = 192`.

**vs. doc:**
- Doc: "no buffering, validate & forward." Current code does that but **also**
  does several non-receiver responsibilities on the data path: header
  injection, transcription kickoff, chat-entry creation, audio-blob save,
  drift correction. The doc's `server audio receiver` should not own any of
  those; they are persistence concerns layered onto the live path.
- The header injection at offset `-1 ms` is an artifact of how `AudioSource`
  is later replayed. In the doc model, codec setup is the receiver's job
  once, not a synthetic frame in the live stream.

## 6. `server stream store`

**Now:**
- `src/dotnet/Streaming.Service/Services/StreamStore.cs` —
  `StreamStore<AudioFrame>`. Inbound stream is wrapped with `stream.Memoize()`,
  which calls `new AsyncMemoizer(source, int.MaxValue, ...)` — the memoizer
  is **unbounded** for the stream's lifetime.
- `ReplayTailSize` defaults to `int.MaxValue` and is **not overridden** when
  `_audioStreams` is constructed in `AudioStreamingBackend.cs:43`. So
  `Get()` calls `memoizer.Replay(int.MaxValue, ct)` — the entire memoized
  history is re-emitted to every consumer.
- Expiration: `ExpirationDelay = AudioSettings.StreamExpirationDelay`, and
  background expiration bumping keeps the entry alive while the memoizer
  runs.

**vs. doc:**
- Doc: the store retains the complete recording stream, matching current
  behavior and the needs of transcription/persistence. There is **no**
  server-side live replay cap in the target.
- The gap is server-side presentation skipping. The target says the server
  makes audio available and sends it; it does not decide which audio is too old
  for presentation. Current `LiveStreamMuxer` still picks a live-start offset
  on the client's behalf using `MaxCatchUpLag = 3 s`, and
  `AudioStreamingBackend.SkipTo` applies that server-side skip.

## 7-8. `server audio muxer` / `server audio sender`

**Now:**
- `AudioStreamingBackend.GetAudio(streamId, skipTo, ct)`
  (`src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.cs:63-74`):
  fetches the memoized stream from `_audioStreams`, applies `SkipTo(stream,
  skipTo, ct)` (preserves the header frame, drops data frames whose `Offset
  < skipTo`), and wraps the result in an `RpcStream<AudioFrame>` with
  `AckPeriod = 64`, `BufferSize = 192` (~3.8 s of credit at 50 fps).
- The actual "fan-out" surface used by live listeners is
  `LiveAudioStreams.GetStream` (which clients call instead of `GetAudio`),
  backed by `LiveStreamMuxer` (covered below). `LiveStreamMuxer.ProcessStream`
  computes `lag = SystemClock.Now - streamInfo.BeginsAt; skipTo = (lag -
  MaxCatchUpLag).Positive()` where `MaxCatchUpLag = 3 s`, then calls
  `StreamServer.GetAudio(streamId, skipTo, ...)`. So the catch-up policy
  lives in the muxer, the skip itself lives in the backend's `GetAudio`,
  and the actual data pull lives in the per-stream RPC stream.
- `LiveStreamMuxer` also implements per-author exclusivity: when a fresher
  same-author stream registers, the older `StreamEntry.StopTokenSource` is
  cancelled. After end, an `EvictionDelay = 4 s` (`MaxCatchUpLag + 1 s`)
  prevents duplicate enlistment via `LiveBackend.List`.
- The output channel is `ChannelExt.Create<LiveStreamItem>(UnboundedFanInOptions)`
  — unbounded fan-in. Items are `LiveStreamStart`, `LiveAudioFrame`,
  `LiveStreamEnd`.

**vs. doc:**
- Doc target: server-to-receiver audio delivery is a muxed, non-realtime
  inbound RPC stream that sends every audio item. Current RPC stream
  construction is directionally aligned with that because it does not use
  `isRealTime`/`canSkipTo`, and the broad mux/demux shape already exists.
- The mismatch is the extra server-side catch-up logic around that stream:
  `LiveStreamMuxer` computes `skipTo = lag - MaxCatchUpLag`, and
  `AudioStreamingBackend.GetAudio` applies it. In the target, this
  presentation decision belongs to the client `audio buffer`, especially when
  video from the same author is playing.
- `LiveStreamMuxer` also owns per-author exclusivity, catch-up, eviction
  debouncing, and three distinct item types. Some of that may remain as stream
  lifecycle fan-out, but catch-up should not live there. The muxer should
  multiplex; it should not skip.
- Unbounded fan-in output channel duplicates retention work already done by
  `StreamStore` and adds another place where backlog can hide.

## 9-10. `audio receiver` / `audio demuxer`

**Now:**
- Live listening goes through
  `src/dotnet/UI.Blazor.App/Services/Streaming/LiveStreamProcessor.cs`,
  which wraps `ILiveAudioStreams.GetStream(...)` in a resilient stream and
  feeds `LiveStreamDemuxer`.
- `LiveStreamDemuxer` (`Services/Streaming/LiveStreamDemuxer.cs`):
  - One unbounded `Channel<ReadOnlyMemory<byte>>` per active stream.
  - On `LiveStreamStart`, creates the channel and raises `StreamStarted`
    with `(LiveStreamInfo, PlaysAt, asyncEnumerable)`.
  - On `LiveAudioFrame`, writes only `frame.Data` to the matching channel
    — the original `frame.Offset` from the server is discarded.
  - On `LiveStreamEnd`, completes the channel.
  - On `LiveStreamReset`, completes all channels and clears them.
- `ChatListener.OnStreamStarted` (`Services/Playback/ChatListener.cs:64-128`):
  - Computes `playAt = Moment.Max(minPlayAt, streamInfo.BeginsAt)` and
    `skipTo = (playAt - streamInfo.BeginsAt).Positive()`.
  - Reconstructs frame offsets by index:
    `Offset = TimeSpan.FromMilliseconds(i * Constants.Audio.OpusFrameDurationMs)`.
  - Skips while `offset < skipTo`, then subtracts `skipTo` from remaining
    offsets.
  - Wraps the result in an `AudioSource` and enqueues it for playback.

**vs. doc:**
- Doc: receiver should not contain a second independent playback buffer and
  should just drain the inbound non-realtime RPC stream into the demuxer, then
  demux into per-stream `audio buffer`s. Current path has multiple buffers and
  skip stages and three transports (RPC frames carry `Offset`, demuxer drops
  `Offset` and forwards bytes, listener reconstructs `Offset` from index).
- The demuxer shape itself is target-aligned: it splits one inbound stream
  into per-stream flows. The mismatch is that offset loss and client-side
  `ChatListener.skipTo` make demux/listener participate in presentation
  skipping. In the target, demux reads and forwards everything; only the
  per-stream `audio buffer` decides whether to speed up or skip.
- The most damaging deviation is the **offset round-trip**: the server
  computed correct frame offsets, the muxer already applied a `skipTo`, then
  the demuxer threw the offsets away, then the listener fabricated new
  offsets from index 0 and applied another `skipTo` over the fabricated
  timeline. The doc's "carry origin capture time end-to-end" model moves the
  presentation decision into the `audio buffer`, where it can use video state.

## 11. `audio buffer`

**Now (this is structurally different from the doc):**
- The intentional playback buffer lives **inside the feeder worklet**, not
  before the decoder. It holds **decoded PCM**, not encoded Opus.
- `feeder-audio-worklet-processor.ts`:
  - `chunks: Denque<Float32Array | 'end'>` — decoded PCM chunks waiting for
    the ring buffer.
  - `buffer = new AudioRingBuffer(8192, 1)` — the inner ring (~170 ms at
    48 kHz, mono).
  - Total `bufferedDuration` = ring + chunks queue.
  - Start threshold (`bufferSizeToStartPlayback`): `AP.BUFFER_TO_PLAY_DURATION = 0.1 s`,
    or `AP.BUFFER_TO_PLAY_DURATION_WITH_VIDEO = 0.5 s` when
    `bufferEscalation > 0` (set when remote video streams are present).
  - On starvation, `bufferSizeToStartPlayback` grows by
    `AP.BUFFER_TO_PLAY_DURATION_DELTA = 0.1 s` each event; shrinks again
    after a stable period.
  - There is no maximum-size cap — `chunks` is an unbounded `Denque`.
- Above the worklet there is a `.NET → JS push throttle`: `AudioTrackPlayer.ProcessMediaFrame`
  (`Components/AudioPlayer/AudioTrackPlayer.cs:121-155`) paces frames at
  real-time for the first `PacingDuration = 150 ms`, then waits for JS
  buffer-low feedback (timeout 10 s). This is a managed-side throttle on top
  of the worklet buffer.
- Above that, `RemoteAudioStreamCache` may cache an entire remote-streamed
  audio source between server hops.

**vs. doc:**
- **The decision-making buffer is in the wrong place.** The doc puts the
  intentional playback buffer before the decoder, owned by a dedicated
  component that sees encoded frames and per-author timing. Current code
  puts it after decode, inside the audio worklet, holding decoded
  `Float32Array` chunks. That placement is structurally wrong for what the
  target needs the buffer to do: the worklet has no per-author origin time,
  no notion of paired video, and no way to make a frame-pattern speed-up
  vs. hard-skip decision — those decisions belong to a buffer that sits
  before decode, knows about author identity, and can read video state.
  The decoder also wastes work on samples that may be skipped.
  A small post-decode smoothing buffer (~100 ms of decoded PCM) will still
  remain inside the renderer to absorb scheduling jitter — but it is purely
  a smoothing buffer with no skip semantics: whatever reaches it plays.
- **Unbounded buffering is partly aligned.** The target now allows the
  audio-only buffer to grow without an upper bound; current `chunks` is
  unbounded, so the shape is right for the audio-only case. What is missing
  is the video-aware policy.
- **Two competing playback buffers.** The .NET-side `AudioTrackPlayer` pacing
  and the JS-side worklet start threshold both regulate playback start, with
  no shared notion of start threshold or video-aligned presentation policy.
  The doc consolidates this into a single `audio buffer` owned by one
  component.
- **Adaptive start threshold drifts upward.** `bufferSizeToStartPlayback`
  grows on starvation and only shrinks after a stable window. Repeated
  starvation can permanently raise startup latency until the player is torn
  down. The doc allows a self-tuning safety margin but expects it to be
  hysteretic and bounded.

## 12. `audio decoder`

**Now:**
- `src/dotnet/UI.Blazor.App/Components/AudioPlayer/workers/opus-decoder-worker.ts`
  — runs in a Web Worker, holds a `Map<string, OpusDecoder>` (decoder per
  stream).
- Each `OpusDecoder` (`workers/opus-decoder.ts`) wraps either system
  `AudioDecoder` or a WASM Opus decoder. Decoding is async; the worker
  schedules `decode(buffer, offset, length)` calls and posts decoded
  `Float32Array` chunks to the feeder worklet via the worker port.
- On abort/end, the pending decode queue can be cleared.
- `audio-player.ts` is the main-thread bridge: it accepts encoded frames and
  posts them to the worker. It silently drops frames if the `AudioContext`
  is not yet ready (`if (!this.context.state ...) return;`).

**vs. doc:**
- Doc: "no buffering, immediately hand decoded samples to renderer." Current
  code is reasonably close in steady state, but:
  - The async processor queue inside `OpusDecoder` is an unintentional
    micro-buffer.
  - The main-thread `audio-player.ts` silent-drop on `context not ready` is a
    decoder-input-side discard that the doc does not anticipate (the doc's
    model assumes the audio buffer absorbs startup).

## 13. `audio renderer`

**Now:**
- `feeder-audio-worklet-processor.ts.process(inputs, outputs, parameters)` is
  the renderer. It runs at audio-thread priority every render quantum (~128
  samples ≈ 2.67 ms at 48 kHz), pulling from the inner `AudioRingBuffer` into
  the output channel.
- During startup it discards `skipSamples` (Opus pre-skip) at stream start
  for decoder correctness.
- On underrun (ring empty and chunks empty) it emits silence and reports
  `bufferState = 'starving'` upstream.
- On `end`, it emits silence, marks itself ended, and releases buffers.

**vs. doc:**
- The renderer largely matches the doc's intent: it is the platform-output
  handoff, emits silence on underrun, and does not rate-shift. The
  worklet's inner `AudioRingBuffer(8192, 1)` (~170 ms at 48 kHz) is
  essentially the small smoothing buffer the doc allows here, slightly
  above the doc's ~100 ms cap.
- What deviates is that the larger decision-making `audio buffer`
  (stage 11) lives inside the same worklet and shares state with the
  smoothing ring, so there is no clean separation between "skip/speed-up
  decisions happen here" and "playback is committed to play here." Once
  the `audio buffer` is moved pre-decode, this worklet is left as the
  pure smoothing buffer the doc describes.

## 14. `audio presentation`

**Now:**
- Web: the `AudioWorkletNode` is connected through the `AudioContext`
  destination; the platform clock owns presentation timing. There is no
  rate-shifting in audio.
- MAUI: `IAudioOutput` per-platform plays the rendered samples through the
  device clock similarly.
- A/V sync: `nodejs/src/audio-video-sync.ts` is the cross-thread broadcast
  hub. `AudioTrackPlayer.OnPlaying` (`AudioTrackPlayer.cs:44-59`) calls
  `blazorApp.AudioVideoSync.update(authorId, offset, recordedAtMs, state)`
  on the JS side. The audio-video-sync module coalesces broadcasts to
  ~60 Hz (`BROADCAST_MIN_INTERVAL_MS = 16`) and clears state on `'ended'`.
  Subscribed video workers (decoder workers / MSTG selectors) re-anchor
  `capturedAt` to their own `performance.now()`.

**vs. doc:**
- Audio presentation itself matches: no rate-shifting, platform clock-driven.
- A/V sync is **inverted vs. doc**. The doc says video establishes the shared
  delay and audio adopts it. Current code: audio publishes its `playingAt`
  via `AudioVideoSync` and **video pulls and chases it** (the video
  pipeline's `WorkerMstgSelector.queue` and `VideoPlayer.renderTick` both
  read `AudioVideoSync` to compute their target timestamp). The relationship
  is the reverse of what the doc prescribes.

## Time Model

**Now:**
- Origin time is partially preserved on the server: each `AudioFrame.Offset`
  is computed from `frameIndex * 20ms` in the producer; `LiveStreamInfo.BeginsAt`
  carries the server-side stream start.
- On the live path, the muxer skips encoded frames according to wall-clock
  lag, the demuxer drops frame offsets entirely, and the listener
  reconstructs synthetic offsets from the post-skip frame index. Audio
  presentation timing is then driven by the platform audio clock once
  playback starts.
- A/V sync uses `AudioVideoSync` as the shared time-source — but it only
  carries audio's `playingAt`; video chases.

**vs. doc:**
- The doc requires every frame to carry origin capture time end-to-end and
  the receiver to build one `origin -> local` mapping per author. Current
  audio path satisfies this on the wire (frame.Offset, BeginsAt) but
  destroys it inside the demuxer. A unified mapping does not exist — there
  are at least three independent skip points (`GetAudio.SkipTo`,
  `LiveStreamMuxer.skipTo`, `ChatListener.skipTo`).
- The doc requires audio to adopt the video target delay when paired.
  Current code does the opposite — `BUFFER_TO_PLAY_DURATION_WITH_VIDEO = 0.5`
  raises the audio start threshold when video is present, but it is audio
  that publishes timing and video that adapts. Reversing this requires
  unwiring `AudioVideoSync` consumption from the video side.
- The updated target also expects late video pairing to force audio catch-up.
  For example, if 30 seconds of audio accumulated while video was unavailable
  and video then resumes in realtime, the receiver should align audio to the
  video presentation point inside the `audio buffer`. It should either
  temporarily speed up audio by dropping a regular frame pattern, or hard-skip
  the stale audio region when the correction is too large.

## Stream Lifecycle

**Now:**
- VAD-driven segmentation matches the doc in spirit: voice-start creates a
  new `AudioStream` on the producer, voice-end completes it. A new
  `PushAudio` is also created on peer-change retry, which produces an
  additional server-side chat entry boundary unrelated to VAD.
- Each `PushAudio` becomes one chat entry on the server, transcribed and
  persisted independently.
- `LiveAudioBackend` (`Streaming.Service/Backend/LiveAudioBackend.cs`) tracks
  active streams in Redis with `StreamTtl = Constants.Audio.MaxStreamDuration = 3 min`
  and per-author eviction of stale registrations.
- On the receiver, end-of-stream is propagated through `LiveStreamMuxer` →
  `LiveStreamDemuxer` → `ChatListener`, but the per-stream channels are
  unbounded and there is no overlap with the next stream from the same
  author.

**vs. doc:**
- Doc-aligned overall, with two friction points:
  1. Peer-change creates a new server entry mid-utterance, which is a
     transport detail leaking into chat-entry semantics. The doc treats stream
     lifecycle as a recording concern; reconnection should resume the same
     logical stream where possible.
  2. The doc allows the receiver to overlap the tail of one stream with the
     head of the next when origin timestamps abut, so listeners do not hear a
     gap. Current `ChatListener` enqueues sequentially with no overlap logic.

## Startup

**Now:**
- Receiver-side startup is split between `LiveStreamMuxer.skipTo` (server
  drops everything older than `MaxCatchUpLag = 3 s`), `ChatListener.skipTo`
  (client further skips by `playAt - BeginsAt`), and `feeder-audio-worklet-processor.tryBeginPlaying`
  (waits until `bufferedDuration >= bufferSizeToStartPlayback`, default
  100 ms or 500 ms with video).
- The Opus header frame at `Offset = -1 ms` is preserved through `SkipTo` so
  the decoder can configure regardless of where the receiver joined.
- `AudioTrackPlayer.PacingDuration = 150 ms` adds a sender-paced ramp on top
  of the JS-side fill threshold.

**vs. doc:**
- Doc: the server sends audio through a non-realtime inbound RPC stream and
  the receiver drains it into the `audio buffer`; the server should not skip
  to the live edge on the receiver's behalf. Current behavior still has
  server-side catch-up: the muxer drops everything older than
  `MaxCatchUpLag = 3 s`, and then the client may skip again.
- Doc: when paired with video, defer audio start until the shared A/V target
  delay is reached. Current code raises the start threshold to 500 ms when
  video is detected (`BUFFER_TO_PLAY_DURATION_WITH_VIDEO`), which is a
  fixed-value approximation rather than a synchronized join.
- The updated target adds a second startup case: video may appear after audio
  has already queued. Current code has no explicit decision between
  speed-up-by-frame-dropping and hard-skip-to-video-time; it only has fixed
  buffer escalation and the existing server/client skip points.

## Hardest Refactorings

In rough order of difficulty:

1. **Stop reconstructing offsets in the demuxer/listener pipeline.** Today
   `LiveStreamDemuxer` writes only `frame.Data` to its per-stream channels
   and `ChatListener` rebuilds offsets by index. Preserving `frame.Offset`
   end-to-end and using it for the single client-side `skipTo` requires
   touching the demuxer contract, every consumer, the playback enqueue path,
   and the speed-based replay skip in `ReplayStreamMuxer`. The replay path
   currently leans on offset reconstruction to skip every Nth frame at
   speed > 1.

2. **Move the playback buffer from inside the feeder worklet to before the
   decoder.** This is aligned in direction with the analogous video refactor
   — both move the playback buffer to a dedicated pre-decode component —
   but it is **not structurally identical**. The audio version is simpler in
   several ways: every audio frame is a uniform 20 ms quantum, there is no
   keyframe-aware trim policy, and the audio-only case explicitly allows
   unbounded growth, so the encoded-frame buffer mostly just owns the
   start-threshold, the A/V-paired catch-up decision (frame-drop speed-up
   vs. hard skip), and the JS buffer-low signal that `AudioTrackPlayer`
   waits on. Today the start threshold, starvation detection,
   escalation-with-video logic, and decoder-output handling all live in
   `feeder-audio-worklet-processor.ts`; this refactor pulls them out and
   puts them on the encoded-frame side.

3. **Remove server-side live catch-up skipping.** Full server-side retention
   stays — transcription and persistence need it. What changes is who decides
   what is too old to present. Today `LiveStreamMuxer` computes
   `skipTo = lag - MaxCatchUpLag` (3 s) and `AudioStreamingBackend.SkipTo`
   applies it; the receiver never sees the skipped audio. The target moves
   that decision into the client `audio buffer`, which can use video state to
   choose between playing everything, temporary speed-up, and hard skip.
   Cascades: `LiveStreamMuxer.MaxCatchUpLag` becomes redundant for live audio
   presentation, `SkipTo` simplifies, and `RemoteAudioStreamCache` assumptions
   need to be revisited.

4. **Make sender upload loss-preserving while removing silent producer drops.**
   The producer-side `RpcStream` should remain non-realtime for recording
   upload, because ACK compaction would delete transcribed audio. The hard
   part is removing or changing the current drop-oldest queues
   (`AE.MAX_BUFFERED_FRAMES` before encode and `AS.MAX_BUFFERED_FRAMES` after
   encode) without allowing unlimited memory growth. Overload should become
   backpressure or an observable recording-quality failure, not silent speech
   deletion.

5. **Invert A/V sync direction.** Today audio publishes its `playingAt`
   through `AudioVideoSync` and video chases it. The doc has video establish
   the shared delay and audio adopt it. The mechanical changes are small
   (move the `update`/`get` polarity), but the implications are large: the
   audio buffer needs to extend on demand to match the video target delay,
   the worklet's `bufferSizeToStartPlayback` becomes a function of paired
   video state, `AudioVideoSync.bufferEscalation` (currently a fixed 0.5 s
   flag) becomes a continuous video-driven delay target, and late video
   pairing needs an explicit choice between speed-up-by-frame-dropping and
   hard skip.

6. **Consolidate the three skip points into one client-side mapping.** Once
   offsets are preserved end-to-end (refactor 1) and server-side live catch-up
   is removed (refactor 3), `AudioStreamingBackend.SkipTo`,
   `LiveStreamMuxer.skipTo`, and `ChatListener.skipTo` become unnecessary for
   normal live playback. The `audio buffer` alone decides whether to play
   received audio as-is, speed up by dropping frames, or hard-skip stale
   chunks. The muxer, inbound RPC stream, and demuxer should read and forward
   all audio items. Removing the skip points requires unwinding the assumption
   baked into `LiveStreamMuxer.ProcessStream` that early skip is a server
   responsibility.

7. **Untangle MAUI platform ring buffers.** Each platform interposes a
   ~10 s ring buffer between capture and the rest of the pipeline, plus
   Windows adds a 40 ms intentional mic delay. Aligning with the doc's
   "no intermediate buffer" intent requires pushing capture-side
   backpressure into the platform layer (or accepting samples being dropped
   loudly rather than silently). The 40 ms AEC delay is hardware-imposed and
   cannot be removed; it just becomes a documented capture-side latency
   floor for Windows.
