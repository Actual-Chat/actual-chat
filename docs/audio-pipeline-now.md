# Current Audio Pipeline

This document describes the current ActualChat audio pipeline end-to-end: from capture on the sending device, through RPC/server processing, to presentation on the receiving device. It focuses on buffering, frame dropping, skipping, replay, and catch-up behavior.

The description is intentionally a snapshot of the current implementation. It does not propose the redesign yet.

## Frame Model And Contracts

Audio transport is centered on `AudioFrame`:

- [`AudioFrame`](../src/dotnet/Api/Audio/AudioFrame.cs) inherits from `MediaFrame`.
- `Data` contains the encoded frame bytes.
- `Offset` is the frame offset within the stream.
- `Duration` defaults to `Constants.Audio.OpusFrameDuration`, which is 20ms.
- `IsKeyFrame` defaults to `true`.
- `SerializedData` caches MessagePack-serialized data for forwarding.

Unlike video, audio has no keyframe dependency in practice: every audio frame is treated as a keyframe. Skipping audio frames can still cause audible discontinuities and timing drift, but it does not create the same "missing keyframe corrupts following frames" failure mode as video.

The main RPC contract is [`IStreamServer`](../src/dotnet/Api.Contracts/Streaming/IStreamServer.cs):

- `GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)` returns `RpcStream<AudioFrame>?`.
- `PushAudio(Session session, string chatId, string? repliedChatEntryId, double clientStartOffset, int preSkip, RpcStream<AudioFrame> frameStream, CancellationToken cancellationToken)` uploads a live recording segment.

`PushAudio` is configured with:

- `RemoteExecutionMode.AwaitForConnection`
- `RemoteExecutionMode.AllowReconnect`

It does not use `AllowResend`.

Audio stream ACK behavior is configured by constants in [`Constants.Audio`](../src/dotnet/Api/Constants.Audio.cs):

- `StreamAckPeriod = 64`
- `StreamAckAdvance = 192`

Those ACK settings provide RPC-level flow-control behavior, but the audio pipeline also has many additional local buffers and skip/drop policies around the stream.

## Important Constants

Shared .NET constants are in [`Constants.Audio`](../src/dotnet/Api/Constants.Audio.cs):

- Opus frame duration: 20ms.
- Recording sample rate: 16 kHz.
- Playback sample rate: 48 kHz.
- Bit rate: 32 kbps.
- Streaming channel capacity: 1024 frames.
- Recording duration: 30 seconds.
- Max realtime stream drift: 3 seconds.
- Max stream duration: 3 minutes.
- Frame silence timeout: 2 seconds.
- Low playback buffer duration: 10 seconds.
- Start playback when buffered duration: 0.1 seconds.

Browser-side constants are in [`_constants.ts`](../src/nodejs/src/_constants.ts):

- Recorder frame duration: 20ms.
- Recorder VAD window: 32ms.
- Audio encoder max buffered frames: 50, about 1 second.
- Audio streamer delay frames: 3, about 60ms.
- Audio streamer max buffered frames: 1500, about 30 seconds.
- Audio playback start buffer: 0.1 seconds.
- Audio playback start buffer with video: 0.5 seconds.
- Low playback buffer: 10 seconds.

## Web Sender Pipeline

### Blazor Entry Point

Recording is initiated by Blazor UI state:

1. [`ChatAudioUI.StateSync`](../src/dotnet/UI.Blazor.App/Services/ChatAudioUI.StateSync.cs) observes active chat state and starts/stops recording.
2. [`AudioRecorder`](../src/dotnet/UI.Blazor.App/Components/AudioRecorder/AudioRecorder.cs) coordinates server time sync, recording state, and audio focus.
3. [`WebRecorderEngine`](../src/dotnet/UI.Blazor.App/Components/AudioRecorder/WebRecorderEngine.cs) calls into JavaScript.
4. [`audio-recorder.ts`](../src/dotnet/UI.Blazor.App/Components/AudioRecorder/audio-recorder.ts) owns the browser-side `AudioRecorder`.

Before mic capture starts, the UI can play a begin tune. After that, the web recorder starts the Opus media recording pipeline.

### Browser Capture Graph

The main browser capture implementation is [`opus-media-recorder.ts`](../src/dotnet/UI.Blazor.App/Components/AudioRecorder/opus-media-recorder.ts).

The intended graph is:

```text
Microphone
  -> VAD AudioWorklet
  -> VAD Worker
  -> Encoder Worker

Microphone
  -> Encoder AudioWorklet
  -> Encoder Worker
  -> AudioStreamer
  -> RPC PushAudio
```

`getUserMedia` requests mono audio with echo cancellation and noise suppression. Auto gain control is disabled on Android. The requested sample rate is normally 16 kHz, except for Firefox-specific handling.

The `AttachedRecordingPipeline` creates:

- an encoder worker,
- a VAD worker,
- an encoder audio worklet,
- a VAD audio worklet,
- message channels between them.

The microphone source is connected to both worklets.

### Encoder Worklet

[`opus-encoder-worklet-processor.ts`](../src/dotnet/UI.Blazor.App/Components/AudioRecorder/worklets/opus-encoder-worklet-processor.ts) receives browser render quanta, typically 128 samples at the current `AudioContext` sample rate.

It uses:

- `AudioRingBuffer(8192, 1)`,
- a 20ms `samplesPerWindow`,
- a small pool of reusable buffers.

The worklet pushes incoming samples into the ring buffer. Whenever there are enough samples for one 20ms frame, it pulls a frame and sends the PCM buffer to the encoder worker.

This is a buffering point, but not normally a drop point. The ring buffer implementation throws on overwrite rather than silently dropping.

### VAD Worklet And Worker

The VAD worklet batches microphone audio into VAD-sized windows and sends them to [`audio-vad-worker.ts`](../src/dotnet/UI.Blazor.App/Components/AudioRecorder/workers/audio-vad-worker.ts).

The VAD worker uses:

- a queue of incoming buffers,
- a ring buffer for VAD input,
- WebRTC VAD initially,
- neural VAD after it loads.

When neural VAD becomes available, the worker clears pending queued input and restarts the worklet with the neural VAD window size. That is an explicit data discard point for VAD analysis. It affects detection timing, not already-encoded audio.

The VAD worker sends voice activity changes to the encoder worker.

### Encoder Worker

[`opus-encoder-worker.ts`](../src/dotnet/UI.Blazor.App/Components/AudioRecorder/workers/opus-encoder-worker.ts) is the main sender-side audio decision point.

It receives PCM frames from the encoder worklet and appends them to a queue. If the queue grows beyond `AUDIO_ENCODER.MAX_BUFFERED_FRAMES`, currently 50 frames / about 1 second, it drops the oldest PCM frames.

The worker waits for VAD before starting a recording stream. On voice start:

1. It creates an Opus encoder, either system `AudioEncoder` or WASM Opus.
2. It creates an `AudioStream` through `AudioStreamer.addStream(...)`.
3. It processes the queued pre-roll frames.

Pre-roll processing trims leading low-gain/silent frames before encoding. This is an intentional skip/drop point at voice start. The implementation keeps a small amount of context so speech is not cut too aggressively.

On voice end:

1. It processes fade-out data.
2. It flushes the encoder.
3. It completes the `AudioStream`.
4. It resets VAD/encoder state and clears queued PCM.

### AudioStreamer

[`audio-streamer.ts`](../src/dotnet/UI.Blazor.App/Components/AudioRecorder/workers/audio-streamer.ts) converts encoded Opus packets into an RPC `RpcStream<AudioFrameDto>`.

Each `AudioStream` has:

- a queue of encoded frames,
- a first-frame timestamp based on `ServerClock.now()`,
- a frame pool for buffer reuse,
- an `isCompleted` flag.

When an encoded packet is added:

- it is copied into a pooled buffer,
- pushed onto the queue,
- and, if the queue exceeds `AUDIO_STREAMER.MAX_BUFFERED_FRAMES`, currently 1500 / about 30 seconds, the oldest encoded packet is dropped.

Before sending starts, the stream waits until more than `AUDIO_STREAMER.DELAY_FRAMES` are queued. The current delay is 3 frames, about 60ms. This is an intentional startup buffer.

For each `PushAudio` attempt, `AudioStreamer` constructs a new `RpcStream` whose enumerator yields:

- `Data`: encoded packet bytes,
- `Offset`: `frameIndex * 20ms`,
- `Duration`: 20ms,
- `IsKeyFrame`: `true`.

`frameIndex` is local to the current `PushAudio` call. On peer change/retry, a new `PushAudio` call starts with a fresh frame index. Packets still in the `AudioStream` queue can be sent by the new call, but packets already handed to the old RPC stream and not delivered may be lost because `PushAudio` allows reconnect but does not allow resend.

## Native / MAUI Sender Pipeline

MAUI recording uses [`MauiRecorderEngine`](../src/dotnet/App.Maui/Services/Recording/MauiRecorderEngine.cs).

The engine captures platform PCM audio, runs VAD, encodes Opus, and sends `PushAudio`, similar to the browser pipeline but with different buffering.

### Platform Capture

Android capture is in [`AndroidAudioCapture`](../src/dotnet/App.Maui/Platforms/Android/Audio/AndroidAudioCapture.cs):

- Uses Android `AudioRecord`.
- Captures mono 16 kHz `PCM_FLOAT`.
- Writes captured data into a 10-second `BlockRingBuffer<float>`.
- Writes are fire-and-forget. If the buffer cannot accept data, samples can be dropped.
- The async enumerator yields 20ms frames.

Windows capture is in [`WindowsAudioCapture`](../src/dotnet/App.Maui/Platforms/Windows/Audio/WindowsAudioCapture.cs):

- Uses WinRT `AudioGraph` for microphone capture.
- Uses WebRTC audio processing for echo cancellation, noise suppression, AGC, and high-pass filtering.
- Uses microphone, loopback, and output ring buffers.
- Adds a 40ms microphone delay to align with loopback for echo cancellation.
- Writes processed output into a 10-second ring buffer.
- If buffers fill, samples can be dropped.
- The async enumerator yields 20ms frames.

iOS capture is in [`IosAudioCapture`](../src/dotnet/App.Maui/Platforms/iOS/Audio/IosAudioCapture.cs):

- Uses `AVAudioEngine`.
- Enables voice processing.
- Resamples to the voice recording format.
- Writes into a 10-second ring buffer.
- If there is insufficient remaining capacity, it logs and drops samples.
- The async enumerator yields 20ms frames.

### MAUI Recorder Processing

`MauiRecorderEngine.AudioStreamProcessor` keeps:

- a 2-second VAD buffer,
- a 500ms encoding pre-roll buffer,
- a current `AudioStream` channel,
- a VAD state machine.

For each captured PCM frame:

1. It writes data into the VAD buffer.
2. It writes data into the encoding pre-roll buffer.
3. If voice is not active and the encoding buffer is full, it discards oldest 20ms frames to keep bounded pre-roll.
4. It processes VAD in batches of 3 * 32ms, about 96ms.

On VAD start:

1. It trims the pre-roll buffer using gain analysis.
2. It creates a bounded audio stream channel.
3. It starts Opus encoding and sending.

On VAD end:

1. It stops the encode/send worker.
2. It completes the stream.

The MAUI send channel is bounded with `Constants.Audio.StreamingChannelCapacity`, currently 1024 frames. The channel uses backpressure rather than dropping while active. On completion, the code drains remaining frames, so unsent frames can be discarded at stream end.

The MAUI retry behavior is similar to web: a retry creates a new `PushAudio` stream over the remaining channel data.

## Server Ingress And Live Publication

### PushAudio

[`StreamServer.PushAudio`](../src/dotnet/Streaming.Service/Services/StreamServer.cs) receives uploaded audio.

It:

1. Parses chat and reply IDs.
2. Creates a new stream ID.
3. Creates an `AudioRecord`.
4. Wraps the incoming `RpcStream<AudioFrame>`.
5. Calls `AudioStreamingBackend.ProcessAudio(...)`.
6. Disconnects the incoming stream in `finally`.

Each `PushAudio` call corresponds to a server-side audio stream/recording segment. Because browser and MAUI recording are VAD-segmented, one user speech sequence can become multiple `PushAudio` calls if VAD starts/stops multiple times.

### Backend Processing

[`AudioStreamingBackend.ProcessAudio`](../src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs) handles validation, live publication, transcription, and persistence.

Important behavior:

- It wraps incoming frames with a frame silence watchdog. If no frame arrives for `Constants.Audio.FrameSilenceTimeout`, currently 2 seconds, processing is cancelled.
- It validates permissions and author identity.
- It computes `beginsAt` from `clientStartOffset`.
- If client/server drift is too large, it falls back to server time.
- It creates an `AudioSource` from the incoming frames.
- It creates an Opus header frame at offset `-1ms`.
- It prepends the header to the audio frame stream.
- If voice streaming is enabled for the chat, it registers the stream in `LiveAudioBackend`.
- It publishes live frames into `StreamStore<AudioFrame>`.
- It starts transcription.
- Once transcript text is available, it creates a chat entry.
- When the audio stream completes, it saves the audio blob/media and unregisters the live stream.

### AudioSource And Memoization

[`AudioSource`](../src/dotnet/Api/Audio/AudioSource.cs) applies `skipTo` by skipping frames whose offset is lower than the requested position, then normalizing offsets by subtracting `skipTo`.

`AudioSource` inherits from [`MediaSource`](../src/dotnet/Api/Media/MediaSource.cs), which memoizes the frame stream:

```text
source frames -> AsyncMemoizer -> replayable frame stream
```

[`AsyncMemoizer`](../src/dotnet/Core/Async/AsyncMemoizer.cs) stores items in a linked list. Unless a capacity is specified, replay is effectively unbounded for the lifetime of the memoizer.

### StreamStore

[`StreamStore<T>`](../src/dotnet/Streaming.Service/Services/StreamStore.cs) is the live in-memory stream sharing layer.

For audio:

- The backend publishes the live frame stream into a `StreamStore<AudioFrame>`.
- `Publish` memoizes the stream.
- `ReplayTailSize` defaults to `int.MaxValue`.
- `Get` waits briefly for a stream to be published.
- While the memoizer is running, expiration is extended in the background.

This is one of the largest live buffering layers. A late consumer can replay from the memoized stream unless later `skipTo` logic skips forward.

### GetAudio And SkipTo

[`AudioStreamingBackend.GetAudio`](../src/dotnet/Streaming.Service/Backend/AudioStreamingBackend.cs) fetches a stream from `StreamStore` and applies `SkipTo`.

`SkipTo` preserves the header frame if present, then skips data frames while `frame.Offset < skipTo`.

For remote streams, `StreamServer.GetAudio` can fetch audio from a remote backend and store it locally in `RemoteAudioStreamCache`, then apply local skip. That remote path adds another memoized/cache layer before delivery.

## Live Audio Metadata

[`LiveAudioBackend`](../src/dotnet/Streaming.Service/Backend/LiveAudioBackend.cs) does not carry audio bytes. It stores active live stream metadata in Redis:

- chat ID,
- author ID,
- stream ID,
- begin time,
- audio format,
- TTL/expiration state.

It also evicts stale streams for the same author when registering a new one.

## Live Receive Pipeline

### LiveAudioStreams

The receiving client does not usually call `IStreamServer.GetAudio` directly for live listening. It calls [`ILiveAudioStreams.GetStream`](../src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs), implemented by [`LiveAudioStreams`](../src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs).

`LiveAudioStreams.GetStream`:

1. Creates or replaces a per-session/per-chat `LiveStreamMuxer`.
2. Returns `RpcStream<LiveStreamItem>`.
3. Uses audio ACK period/advance constants.
4. Sets `AllowReconnect = false` for the returned stream.

The client wraps this in a resilient stream on the UI side.

### LiveStreamMuxer

[`LiveStreamMuxer`](../src/dotnet/Streaming.Service/Services/LiveStreamMuxer.cs) multiplexes active live audio streams in a chat.

It:

1. Watches `LiveAudioBackend.List(chatId)`.
2. Starts one processing task per active stream.
3. Ensures only one stream per author is active.
4. Cancels older same-author streams when newer streams appear.
5. Calls `StreamServer.GetAudio(streamId, skipTo, ...)`.
6. Emits `LiveStreamStart`.
7. Emits `LiveAudioFrame` items.
8. Emits `LiveStreamEnd`.

The muxer has an important catch-up rule:

```text
lag = now - streamInfo.BeginsAt
skipTo = max(lag - MaxCatchUpLag, 0)
```

`MaxCatchUpLag` is 3 seconds. If the receiver starts late, the muxer skips toward the live edge rather than replaying the entire live stream.

The muxer output channel is an unbounded fan-in channel.

### LiveStreamProcessor And Demuxer

The Blazor client uses [`LiveStreamProcessor`](../src/dotnet/UI.Blazor.App/Services/Streaming/LiveStreamProcessor.cs), which wraps `ILiveAudioStreams.GetStream(...)` in a resilient stream and feeds [`LiveStreamDemuxer`](../src/dotnet/UI.Blazor.App/Services/Streaming/LiveStreamDemuxer.cs).

The demuxer:

- creates an unbounded channel per live stream,
- raises `StreamStarted`,
- writes incoming frame data into the matching channel,
- completes the channel on stream end,
- flushes all channels on `LiveStreamReset`.

Important detail: `LiveAudioFrame` carries an `Offset`, but the demuxer writes only `frame.Data` to the per-stream channel. The offset is ignored by the client playback path.

### ChatListener

[`ChatListener`](../src/dotnet/UI.Blazor.App/Services/Playback/ChatListener.cs) receives demuxed live streams.

For each stream:

1. It skips local user's own audio unless debug playback is enabled.
2. It computes `playAt`.
3. It computes `skipTo = max(playAt - streamInfo.BeginsAt, 0)`.
4. It reconstructs frame offsets by frame index: `i * 20ms`.
5. It skips frames while the reconstructed offset is below `skipTo`.
6. It creates an `AudioSource`.
7. It enqueues the source into playback.

Because `LiveStreamMuxer` may already have skipped at the server and because the demuxer discards original frame offsets, the client performs another index-based skip over a reconstructed timeline. This is a notable interaction point.

## Playback Pipeline

### .NET Playback Layer

Playback is coordinated by:

- [`Playback`](../src/dotnet/Api/MediaPlayback/Playback.cs)
- [`TrackPlayer`](../src/dotnet/Api/MediaPlayback/TrackPlayer.cs)
- [`AudioTrackPlayer`](../src/dotnet/UI.Blazor.App/Components/AudioPlayer/AudioTrackPlayer.cs)
- [`WebAudioPlaybackEngine`](../src/dotnet/UI.Blazor.App/Components/AudioPlayer/WebAudioPlaybackEngine.cs)

`TrackPlayer` has a bounded command queue with `DropOldest`. This applies to playback commands, not audio frames.

`AudioTrackPlayer` pushes frames to the JS playback engine. It has an initial pacing period of 150ms. After that, it waits for JS buffer-low feedback:

- if JS reports buffer low, .NET continues pushing;
- if JS reports buffer not low, .NET waits;
- if feedback does not arrive within a timeout, it resumes.

This is a managed-side playback throttle layered on top of the JS audio buffer.

### JavaScript AudioPlayer

[`audio-player.ts`](../src/dotnet/UI.Blazor.App/Components/AudioPlayer/audio-player.ts) owns browser playback.

It:

1. Creates an `AudioContext`.
2. Creates a feeder audio worklet.
3. Initializes the Opus decoder worker.
4. Sends encoded Opus frames to the decoder worker.
5. Receives feeder state changes and reports playback state to .NET.

`audio-player.ts` has a direct frame drop point: if the audio context exists but is not ready, incoming `frame(bytes)` calls return without forwarding the frame to the decoder.

### Decoder Worker

The decoder worker is implemented by:

- [`opus-decoder-worker.ts`](../src/dotnet/UI.Blazor.App/Components/AudioPlayer/workers/opus-decoder-worker.ts)
- [`opus-decoder.ts`](../src/dotnet/UI.Blazor.App/Components/AudioPlayer/workers/opus-decoder.ts)

It decodes Opus packets into PCM using either:

- system `AudioDecoder`, or
- WASM Opus decoder.

The decoder has an async processor queue. On abort/end, the queue can be cleared.

### Feeder Worklet

[`feeder-audio-worklet-processor.ts`](../src/dotnet/UI.Blazor.App/Components/AudioPlayer/worklets/feeder-audio-worklet-processor.ts) is the final browser audio buffer.

It has:

- a queue of decoded PCM chunks,
- an `AudioRingBuffer(8192, 1)`,
- a start-buffer threshold,
- starvation detection,
- codec pre-skip handling,
- buffer-low reporting.

Startup buffer behavior:

- normally starts after 0.1 seconds buffered,
- starts after 0.5 seconds when `bufferEscalation > 0`, which is used when remote video streams are present,
- can grow after starvation by 0.1 seconds,
- can shrink after a stable period without starvation.

During processing:

- if not playing, it emits silence;
- if the ring buffer has enough PCM, it pulls audio;
- if the ring is low, it pulls chunks into the ring;
- if no chunks are available, it emits silence and reports starvation;
- on `end`, it emits silence, marks ended, and releases remaining buffers;
- it discards `preSkip` samples at stream start for Opus decoder correctness.

This is the final and most user-visible buffering/starvation layer.

## Replay Pipeline

Historical playback uses:

- [`ChatReplayer`](../src/dotnet/UI.Blazor.App/Services/Playback/ChatReplayer.cs)
- [`ReplayStreamProcessor`](../src/dotnet/UI.Blazor.App/Services/Streaming/ReplayStreamProcessor.cs)
- [`ReplayStreamMuxer`](../src/dotnet/Streaming.Service/Services/ReplayStreamMuxer.cs)

`ReplayStreamMuxer` reads historical chat entries, resolves the replay start position, downloads audio blobs, and emits `LiveStreamItem` objects similar to live playback.

Replay-specific skip behavior:

- It can skip to the resolved start position.
- It adjusts gaps between entries.
- At playback speeds greater than 1.0, it explicitly skips audio frames. It computes a skip interval and skips every Nth frame.

That speed-based frame skipping is destructive and applies to replay, not normal live listening.

## Saved Audio And Conversion

Saved audio is handled by source converters such as:

- [`ActualOpusStreamConverter`](../src/dotnet/Api/Audio/ActualOpusStreamConverter.cs)
- [`WebMStreamConverter`](../src/dotnet/Api/Audio/WebMStreamConverter.cs)

Actual Opus and WebM conversion can group frames into chunks while saving or reading media. This is buffering for persistence/conversion, not live catch-up logic.

## Legacy SignalR Path

[`StreamHub`](../src/dotnet/Streaming.Service/Services/StreamHub.cs) contains an obsolete SignalR audio upload path. It converts byte chunks into `AudioFrame`s with offsets based on `i * 20ms` and then calls the same backend.

It is marked obsolete in favor of `IStreamServer.PushAudio` via RPC, so it should not be treated as the main path for current clients.

## Buffering, Dropping, And Skipping Inventory

### Sender-Side Web

1. Browser and `AudioContext` introduce implicit capture/render buffering.
2. Encoder worklet has an 8192-sample ring buffer.
3. VAD worklet has an 8192-sample ring buffer.
4. Encoder worker PCM queue is capped at about 1 second and drops oldest PCM frames when full.
5. VAD worker can clear queued VAD input when switching VAD implementation.
6. Voice-start pre-roll trimming discards leading low-gain/silent frames.
7. `AudioStream` encoded packet queue is capped at about 30 seconds and drops oldest encoded frames when full.
8. `AudioStream` waits about 60ms before starting to send.
9. RPC reconnect can preserve queued unsent packets, but packets already handed to a failed stream may be lost on peer change.

### Sender-Side MAUI

1. Platform capture ring buffers are commonly 10 seconds.
2. Platform capture writes can drop samples when buffers are full.
3. Windows adds APM and a 40ms mic delay for echo cancellation.
4. Recorder VAD buffer is 2 seconds.
5. Recorder encoding pre-roll buffer is 500ms.
6. When not voice-active, oldest pre-roll samples are discarded to keep the pre-roll bounded.
7. VAD start trims the pre-roll again.
8. The send channel is bounded at 1024 frames and backpressures while active.
9. Stream completion drains/discards remaining unsent channel frames.

### Server-Side

1. RPC stream ACK period/advance creates RPC-level flow control.
2. `AudioSource` memoizes frame streams.
3. `StreamStore<AudioFrame>` memoizes live streams and defaults to replaying the full tail.
4. `AudioStreamingBackend.SkipTo` skips data frames before `skipTo` while preserving the header frame.
5. `LiveStreamMuxer` skips toward the live edge if lag exceeds 3 seconds.
6. `LiveStreamMuxer` cancels/replaces older streams from the same author.
7. Remote audio fetching can cache/memoize a remote stream before applying local skip.
8. The frame silence watchdog cancels streams with no frames for 2 seconds.

### Receiver-Side Live

1. `LiveStreamProcessor` wraps live streams in a resilient stream.
2. `LiveStreamDemuxer` uses unbounded per-stream channels.
3. `LiveStreamDemuxer` flushes all active channels on reset.
4. `LiveStreamDemuxer` drops original `LiveAudioFrame.Offset` information.
5. `ChatListener` reconstructs offsets by frame index.
6. `ChatListener` may apply another `skipTo` over the reconstructed offset timeline.
7. `AudioSource` memoizes playback frames.

### Receiver-Side Playback

1. `TrackPlayer` command queue is bounded and drops oldest commands when full.
2. `AudioTrackPlayer` has a 150ms initial pacing period.
3. `AudioTrackPlayer` throttles based on JS buffer-low feedback.
4. `audio-player.ts` drops incoming frames if the audio context is not ready.
5. Decoder worker queues encoded frames for async decoding.
6. Decoder queue can be cleared on abort.
7. Feeder worklet queues decoded PCM chunks.
8. Feeder worklet has an 8192-sample ring buffer.
9. Feeder worklet waits for 0.1 seconds buffered before normal playback start.
10. Feeder worklet waits for 0.5 seconds buffered when playback buffer escalation is active.
11. Feeder worklet grows its start buffer after starvation.
12. Feeder worklet discards Opus `preSkip` samples.
13. Feeder worklet emits silence during pauses, startup, starvation, and after end.

### Replay

1. Replay start resolution can skip into the first selected audio entry.
2. Replay adjusts gaps between entries.
3. Replay speed greater than 1.0 explicitly skips every Nth frame.

## Current Interaction Risks

The current audio pipeline has multiple independent mechanisms that can each decide to buffer, skip, drop, or replay:

- sender-side VAD segmentation,
- sender-side PCM and encoded queues,
- RPC stream flow control,
- server-side memoization,
- server-side live catch-up skip,
- client-side resilient reset,
- client-side offset reconstruction,
- playback buffer-low throttling,
- JS feeder starvation recovery.

Because these mechanisms are not controlled by one policy, they can interact in surprising ways.

The most notable interaction is in the live path:

1. The server muxer may call `GetAudio(..., skipTo)` to start near the live edge.
2. `GetAudio` applies skip based on original audio frame offsets.
3. `LiveAudioFrame.Offset` is then sent to the client.
4. The client demuxer ignores that offset and forwards only bytes.
5. `ChatListener` reconstructs offsets by frame index.
6. `ChatListener` can apply its own `skipTo` over the reconstructed timeline.

That means live playback timing no longer fully reflects the original server-side frame offsets after demuxing.

Another major point is `StreamStore<AudioFrame>`: it memoizes live frames with a default full replay tail. Late consumers are then corrected by later skip logic rather than by a single live/realtime stream policy.

## Initial Simplification Targets

These are not redesign decisions, but they are the places most likely to matter when simplifying the pipeline:

1. Decide which layer owns live-edge catch-up for audio.
2. Revisit whether `StreamStore<AudioFrame>` should replay an unbounded live tail.
3. Preserve or intentionally discard `LiveAudioFrame.Offset`; avoid accidental offset reconstruction if server offsets matter.
4. Consolidate sender-side drop policy between encoder queue, encoded packet queue, and RPC stream behavior.
5. Revisit JS playback buffering so .NET pacing, JS buffer-low feedback, and feeder starvation recovery do not compete.
6. Treat replay speed-based frame skipping separately from live audio behavior.
7. Check whether audio RPC streams should use a realtime stream mode/flag directly rather than relying on surrounding custom buffering.

