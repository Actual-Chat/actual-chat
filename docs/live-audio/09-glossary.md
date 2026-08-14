# 09 — Glossary

Quick lookup for terms, types, and abbreviations used across the
live-audio docs.

## Concepts

- **VAD** — Voice Activity Detection. Two implementations: WebRTC VAD
  (always loaded) and Silero / Neural VAD (ONNX, loaded after 2 s).
- **Pre-roll** — small ring of recently encoded frames prepended when
  VAD says "voice start" so the first syllable isn't clipped.
  `VoicePreRollFrameLimit ≈ 15 frames = 300 ms`.
- **PreSkip** — Opus codec warm-up samples (typically 312 at 48 kHz).
  Sender reports it; receiver discards that many decoded samples at the
  start of a stream.
- **AudioStream (TS)** — `audio-streamer.ts` representation of one
  voice segment from start to end. Each maps to one server-side
  `StreamId` and (after transcription) one chat entry.
- **OpenAudioSegment / ClosedAudioSegment** — server-side segment
  states. Open while frames are arriving; closed once the source
  ends and `AudibleDuration` is known.
- **Memoizer** — server-side rolling buffer (`AsyncMemoizer<AudioFrame>`)
  inside `StreamStore<AudioFrame>`. Holds frames so multiple subscribers
  share one decode + late joiners can replay from the start.
- **MSTG** — `MediaStreamTrackGenerator`. Used in the video pipeline;
  not used by audio. Audio uses `AudioWorkletNode` directly.
- **Feeder worklet** — the `AudioWorkletProcessor` that owns the final
  ring buffer and writes PCM to `AudioContext.destination`.
- **Listening / Replaying** — the two `ChatPlayerKind`s.
  Listening = live multiplex via `LegacyGetStream`. Replaying = blob-
  backed via `GetReplayStream`.
- **Source vs server time** — every audio unit carries a `SourceBeginsAt`
  (sender's wall-clock at stream start) and a `BeginsAt` (server's
  wall-clock at first frame). Drift > 5 s ⇒ server overrides
  `SourceBeginsAt`.
- **A/V sync** — receiver-side feature where audio adopts video's target
  presentation delay; if drift, audio either hard-skips (≥ 2 s) or
  speeds up (drop every 4th frame). **Currently disabled** by default
  via `ChatAudioUI.IsAudioSyncEnabled = false`.

## TS types

| Type | File | Purpose |
|---|---|---|
| `OpusMediaRecorder` | `opus-media-recorder.ts` | Main-thread façade: getUserMedia + AudioContext + workers |
| `AudioRingBuffer` | `audio-ring-buffer.ts` | Lock-free float32 ring buffer (used by both worklets) |
| `OpusEncoderWorkletProcessor` | `opus-encoder-worklet-processor.ts` | Audio-thread: 20 ms windows → encoder worker |
| `AudioVadWorkletProcessor` | `audio-vad-worklet-processor.ts` | Audio-thread: 30/32 ms windows → VAD worker |
| `OpusEncoderWorker` | `opus-encoder-worker.ts` | Web Worker: Opus encode + AudioStream lifecycle |
| `AudioVadWorker` | `audio-vad-worker.ts` | Web Worker: WebRTC + Silero VAD |
| `AudioStream` / `AudioStreamer` | `audio-streamer.ts` | Per-voice-segment RPC pump |
| `RecordingActivity` | `recording-activity.ts` | Lit component, mic SVG state |
| `AudioPlayer` | `audio-player.ts` | Main-thread façade per playing track |
| `OpusDecoder` / `EncodedFrameBuffer` | `workers/opus-decoder.ts` | Per-track decode + jitter buffer |
| `OpusDecoderWorker` | `workers/opus-decoder-worker.ts` | Singleton Web Worker hosting decoders |
| `FeederAudioWorkletProcessor` | `worklets/feeder-audio-worklet-processor.ts` | Audio-thread: ring buffer + render |
| `FeederAudioWorkletNode` | `worklets/feeder-audio-worklet-node.ts` | Main-thread side of the feeder worklet |
| `AudioContextSource` / `AudioContextRef` | `services/audio-context-source.ts` | Reference-counted AudioContext manager |
| `FeederNodeTrait`, `DemandInteractiveUI`, `DestinationFallbackTrait` | `services/audio-context-traits.ts` | Behaviours composed onto a context ref |
| `AudioStreamFader` | `services/audio-stream-fader.ts` | Crossfade window (currently dormant) |

## .NET types

| Type | Project | Purpose |
|---|---|---|
| `AudioFrame` | `Api/Audio/` | Wire frame: `Data, Offset, Duration, IsKeyFrame` |
| `AudioFormat` | same | `Codec, SampleRate, ChannelCount, PreSkip, CodecSettings` |
| `AudioCodecKind` | same | enum: `Wav, Flac, Opus` (only Opus used live) |
| `AudioSource` / `AudioSourceExt` | same | In-memory abstraction of a live or stored stream |
| `CachingAudioFrameFormatter` | same | MessagePack formatter, serialize-once caching |
| `ActualOpusStream*` | same | Custom binary container (live RPC) |
| `OggOpusStreamConverter` + `Ogg/*` | same | Ogg/Opus (transcription only) |
| `WebMStreamConverter` + `WebM/*` | same | EBML/Matroska (blob persistence) |
| `LiveAudioFrame`, `LiveStreamItem`, `LiveStreamStart`, `LiveStreamEnd`, `LiveStreamReset`, `LiveStreamInfo`, `LiveStreamSettings` | `Api/Live/` | Multiplexed live stream union |
| `AudioRecord` | `Streaming.Contracts/` | Publisher session info |
| `LiveAudioBackend` | `Streaming.Service/Backend/` | Sharded chat-wide registry (Redis) |
| `AudioStreamingBackend` + `…ProcessAudio` | same | Per-node ingestion + ProcessFrames-equivalent |
| `OpenAudioSegment` / `ClosedAudioSegment` | `Streaming.Service/Audio/` | Segment state |
| `AudioSegmentLanguage` | same | Language candidate resolution |
| `AudioMetadataEntry` | same | Per-segment metadata |
| `LiveAudioStreams` | `Streaming.Service/Services/` | API-pod façade (`ILiveAudioStreams`) |
| `AudioSegmentSaver` | same | WebM blob upload + Media record creation |
| `LiveStreamMuxer` | same | Per-chat live multiplex |
| `ReplayStreamMuxer` | same | Replay/seek with speed control |
| `StreamStore<T>` | same | Per-node stream registry |
| `RemoteAudioStreamCache` | `Services/RemoteStreamCaches.cs` | Cross-shard fan-out cache |
| `AudioProcessorBase` | same | Base for transcription processors |
| `AudioSourceDownloader` | `Core.Server/Blobs/` | Blob → AudioSource read path |
| `Transcribers/{TranscriberFactory,GoogleTranscriber,DeepgramTranscriber,FakeTranscriber,OpenAITranscriber}` | `Streaming.Service/Services/Transcribers/` | Transcription providers |
| `IAudioCatchUpPolicy` / `LiveAudioCatchUpPolicy` | `UI.Blazor.App/Services/Playback/` | A/V sync correction policy |
| `PlaybackLagTracker` | `UI.Blazor.App/Services/` | EMAs of audio + video presentation lag, keyed by author |
| `ChatPlayer` / `ChatListener` / `ChatReplayer` | `UI.Blazor.App/Services/Playback/` | Per-chat playback orchestrators |
| `ChatAudioUI` (+ `.Players`, `.StateSync`) | `UI.Blazor.App/Services/` | Top-level toggle + state |
| `AudioRecorder` | `UI.Blazor.App/Components/AudioRecorder/` | C# recorder façade |
| `WebRecorderEngine` | same | Blazor → JS bridge |
| `AudioTrackPlayer` | `UI.Blazor.App/Components/AudioPlayer/` | One per playing track |
| `WebAudioPlaybackEngine` | same | Blazor → JS bridge |

## RPC methods at a glance

`ILiveAudioStreams`:

| Method | Direction | Purpose |
|---|---|---|
| `PushStream` | Publisher → server | Open the publish stream |
| `GetStream` | Subscriber → server | Per-stream pull (`RpcStream<AudioFrame>`) |
| `GetTranscriptStream` | Subscriber → server | Per-stream live transcript (`RpcStream<TranscriptDiff>`) |
| `LegacyGetStream` | Subscriber → server | Per-chat live multiplex (`RpcStream<LiveStreamItem>`) |
| `GetReplayStream` | Subscriber → server | Per-chat replay with speed |
| `ChangeSettings` | Subscriber → server | Update `LiveStreamSettings` for an active subscription |
| `List` | Subscriber → server | Active streams in chat (Fusion compute) |
| `ReportAudioLatency` | Subscriber → server | E2E latency telemetry |

`IAudioStreamingBackend` (backend, used internally):

| Method | Purpose |
|---|---|
| `PushAudio` | Backend handler for `PushStream` |
| `GetAudio` | Read from local `StreamStore` (used by `LiveAudioStreams.GetStream` and cross-shard fetches) |

`ILiveAudioBackend` (backend, sharded):

| Method | Purpose |
|---|---|
| `Register` / `Unregister` | Add/remove `LiveStreamInfo` in chat |
| `List` | Active streams (Fusion compute) |

## Source-tree map

```
src/dotnet/
├ Api.Contracts/Streaming/
│  └ ILiveAudioStreams.cs
├ Api/
│  ├ Audio/
│  │  ├ AudioFrame.cs, AudioFormat.cs, AudioCodecKind.cs, AudioSettings.cs
│  │  ├ AudioSource.cs, AudioSourceExt.cs, RecordingStreamExt.cs
│  │  ├ CachingAudioFrameFormatter.cs
│  │  ├ ActualOpusStream{Converter,Header}.cs
│  │  ├ IAudioStreamConverter.cs, AudioStreamConverterExt.cs
│  │  ├ OggOpusStreamConverter.cs, Ogg/*
│  │  └ WebMStreamConverter.cs, WebM/*
│  ├ Live/
│  │  ├ LiveAudioFrame.cs, LiveStreamItem.cs
│  │  ├ LiveStreamStart.cs, LiveStreamEnd.cs, LiveStreamReset.cs
│  │  ├ LiveStreamInfo.cs, LiveStreamSettings.cs
│  ├ MediaPlayback/* (TrackPlayer, Playback, PlayerCommands, …)
│  ├ MediaRpcStreamOptions.cs
│  └ Constants.Audio.cs, AppConstants.Audio.cs
├ Core.Audio/
│  ├ {Onnx,Noop}VoiceActivityDetector.cs, VoiceActivityChange.cs, VadResult.cs
│  └ APM/   (currently unused)
├ Streaming.Contracts/
│  ├ AudioRecord.cs
│  ├ IAudioStreamingBackend.cs, ILiveAudioBackend.cs
│  └ ITranscriber.cs, ITranscriberFactory.cs, TranscriberExt.cs
├ Streaming.Service/
│  ├ Audio/{Open,Closed}AudioSegment.cs, AudioSegmentLanguage.cs, AudioMetadataEntry.cs
│  ├ Backend/
│  │  ├ AudioStreamingBackend.cs, AudioStreamingBackend.ProcessAudio.cs
│  │  ├ LiveAudioBackend.cs
│  │  └ StreamEnumerableExt.cs
│  ├ Services/
│  │  ├ LiveAudioStreams.cs
│  │  ├ AudioProcessorBase.cs, AudioSegmentSaver.cs
│  │  ├ LiveStreamMuxer.cs, ReplayStreamMuxer.cs
│  │  ├ StreamStore.cs, StreamHub.cs, RemoteStreamCaches.cs
│  │  └ Transcribers/{TranscriberFactory,Google,Deepgram,Fake,OpenAI,DeepgramOffline}*.cs
│  └ Module/StreamingServiceModule.cs
└ UI.Blazor.App/
   ├ Components/AudioRecorder/
   │  ├ AudioRecorder.cs, AudioRecorderState.cs
   │  ├ WebRecorderEngine.cs, IAudioRecorderEngine.cs
   │  ├ IAudioRecorderBackend.cs, IAudioCodec.cs
   │  ├ AudioRecorderException.cs
   │  ├ RecordingActivityClient.cs
   │  ├ WebMicrophonePermissionHandler.cs
   │  ├ audio-recorder.ts, audio-recorder-state.ts
   │  ├ audio-ring-buffer.ts
   │  ├ opus-media-recorder.ts, opus-media-recorder-contracts.ts
   │  ├ recording-activity.ts, web-microphone-permission-handler.ts
   │  ├ workers/{audio-streamer,opus-encoder-worker,audio-vad-worker,
   │  │           resampler-loader,worker-connectivity-ui}.ts
   │  ├ workers/vad_batched.ort, ort-wasm-simd.{wasm,mjs}
   │  └ worklets/{audio-vad,opus-encoder}-worklet-processor.ts
   ├ Components/AudioPlayer/
   │  ├ AudioTrackPlayer.cs, AudioTrackPlayerFactory.cs
   │  ├ IAudioPlaybackEngine.cs, IAudioPlaybackEngineFactory.cs, IAudioPlayerBackend.cs
   │  ├ WebAudioPlaybackEngine.cs, WebAudioPlaybackEngineFactory.cs
   │  ├ audio-player.ts
   │  ├ workers/opus-decoder-worker*.ts, opus-decoder.ts
   │  └ worklets/feeder-audio-worklet-{processor,node,contract}.ts
   ├ Components/ChatAudioPanel/
   │  ├ ChatAudioPanel.razor, RecorderToggle.razor, PlaybackToggle.razor
   │  ├ VoiceSettings*.razor, VideoToggle.razor
   │  └ {recorder-toggle,playback-toggle}.ts, *-svg.lit.ts
   └ Services/
      ├ AudioInitializer.cs, audio-initializer.ts
      ├ ChatAudioUI.cs, ChatAudioUI.{Players,StateSync}.cs
      ├ ChatAudioState.cs
      ├ IAudioInfoBackend.cs
      ├ PlaybackLagTracker.cs
      ├ Playback/{ChatPlayer,ChatListener,ChatReplayer,ChatAudioTrackInfo,
      │           AudioFramesExt,IAudioCatchUpPolicy}.cs
      └ {audio-context-source,audio-context-traits,audio-stream-fader}.ts
```
