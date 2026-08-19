# Live Audio Pipeline — Documentation Index

This folder documents the **current** live-audio pipeline end-to-end, from
microphone capture in browser A through the server (persistence + fan-out +
transcription) to the speakers in browser B. It is written from current
source code under `src/` only.

Companion folder: [`docs/live-video/`](../live-video/README.md) for the video
pipeline. The two share architectural patterns (sharded backend, RpcStream
fan-out, RemoteStreamCache, AIMD-style controls) but audio diverges in
important ways — it is **loss-preserving**, drives transcription, and is
the side that adapts in A/V sync.

The **PTT** layer ([doc 10](./10-push-to-talk.md)) rides on this very
pipeline: it adds a wake push, a headless playback scope and a push-to-talk
reply on the mobile apps, but every frame it produces or consumes still travels
the capture → publish → fan-out → playback path described in docs 02–07.

## Reading order

| # | Doc | Scope |
|---|-----|-------|
| 1 | [01-end-to-end.md](./01-end-to-end.md) | Browser → server → browser walkthrough |
| 2 | [02-recorder.md](./02-recorder.md) | Microphone → AudioWorklet → VAD → Opus encoder → audio-streamer |
| 3 | [03-codec-and-vad.md](./03-codec-and-vad.md) | Opus configuration, WebRTC + Silero VAD, resampling |
| 4 | [04-rpc-and-formats.md](./04-rpc-and-formats.md) | `AudioFrame`, `LiveStreamItem` union, ActualOpus / WebM / OggOpus, RPC tuning |
| 5 | [05-server-publish-and-transcribe.md](./05-server-publish-and-transcribe.md) | `PushStream` → `ProcessAudio` → segments → WebM blob → Google / Deepgram transcription |
| 6 | [06-server-fanout-and-replay.md](./06-server-fanout-and-replay.md) | `LiveAudioBackend`, `LiveStreamMuxer`, `ReplayStreamMuxer`, `RemoteAudioStreamCache` |
| 7 | [07-receiver.md](./07-receiver.md) | Subscribe → opus-decoder worker → feeder worklet → WebAudio |
| 8 | [08-diagnostics-and-tuning.md](./08-diagnostics-and-tuning.md) | Meters, debug hooks, tunable constants |
| 9 | [09-glossary.md](./09-glossary.md) | Glossary of types, files, and abbreviations |
| 10 | [10-push-to-talk.md](./10-push-to-talk.md) | PTT: wake push, headless playback, PTT reply, heard receipts |

For the design-intent vs. current-shape discussion of buffering and A/V
sync — that's in
[`live-video/11-buffering-and-av-sync.md`](../live-video/11-buffering-and-av-sync.md);
the receiver-side audio half of it (the playback-buffer hold,
`PlaybackLagTracker`, and the demuxer's backlog bound) is summarised in
[07-receiver.md](./07-receiver.md) here.

## Top-level architecture

```mermaid
flowchart LR
    subgraph BrowserA["Sender browser"]
        Mic[Microphone]
        VadW[VAD worklet<br/>+ worker<br/>(WebRTC + Silero)]
        EncW[Opus encoder worker<br/>+ worklet]
        Streamer[audio-streamer<br/>RpcStream pump]
    end

    subgraph API["API pod"]
        ILAS[ILiveAudioStreams<br/>PushStream / GetStream<br/>LegacyGetStream / GetReplayStream]
        RAC[RemoteAudioStreamCache]
    end

    subgraph Backend["Backend (sharded)"]
        ASB[AudioStreamingBackend<br/>ProcessAudio]
        Memo[StreamStore<AudioFrame><br/>memoizer]
        LAB[LiveAudioBackend<br/>(Redis state)]
        Mux[LiveStreamMuxer]
        Replay[ReplayStreamMuxer]
        Trans[Transcribers<br/>Google / Deepgram / Fake]
        Saver[AudioSegmentSaver]
        Blob[(blob storage<br/>.webm)]
        Wake[Wake push<br/>FCM / APNs]
    end

    subgraph BrowserB["Receiver browser"]
        Player[audio-player.ts]
        Dec[opus-decoder worker]
        Feed[feeder worklet]
        Out[WebAudio destination<br/>+ MediaSession]
    end

    Mic --> VadW
    Mic --> EncW
    VadW --> EncW
    EncW --> Streamer
    Streamer -- "RpcStream<AudioFrame>" --> ILAS
    ILAS --> ASB
    ASB --> Memo
    ASB <--> LAB
    ASB --> Trans
    ASB --> Saver
    Saver --> Blob
    Memo --> Mux
    Mux --> ILAS
    Replay --> ILAS
    Blob --> Replay
    ILAS -.cross-shard.-> RAC
    RAC -.-> Memo
    ILAS -- "RpcStream<AudioFrame> /<br/>RpcStream<LiveStreamItem>" --> Player
    Player --> Dec
    Dec --> Feed
    Feed --> Out
    Player -- "ReportAudioLatency" --> ILAS
    ASB -. "hasVoice → wake" .-> Wake
    Wake -. "headless playback (doc 10)" .-> Player
```

## Three-line summary

The sender captures 16-kHz mono mic audio, runs WebRTC + Silero VAD to
gate on speech, encodes 20 ms Opus frames in a Web Worker, and pushes
them to the API pod over a non-realtime `RpcStream<AudioFrame>` —
audio is **loss-preserving**, so frames are never dropped server-side.
The backend memoizes the live frames for fan-out, persists the segment
to a `.webm` blob, and feeds the same frame stream to a transcriber
(Google / Deepgram), whose output is published as a separate
`RpcStream<TranscriptDiff>` for live captions. Receivers subscribe via
`ILiveAudioStreams` (per-stream pull or per-chat multiplex), decode in
a shared opus-decoder worker, and play back through a single
`AudioWorklet` ring buffer per author.

## Cheat-sheet: where things live

| Concern | Location |
|---|---|
| C# recorder | `src/dotnet/UI.Blazor.App/Components/AudioRecorder/` (`AudioRecorder.cs`, `WebRecorderEngine.cs`) |
| TS recorder + workers | same folder (`audio-recorder.ts`, `opus-media-recorder.ts`, `workers/`, `worklets/`) |
| C# player | `src/dotnet/UI.Blazor.App/Components/AudioPlayer/` (`AudioTrackPlayer.cs`, `WebAudioPlaybackEngine.cs`) |
| TS player + workers | same folder (`audio-player.ts`, `workers/`, `worklets/`) |
| Chat-level orchestration | `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs`, `Services/Playback/` |
| RPC contracts | `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs`, `Streaming.Contracts/{IAudioStreamingBackend,ILiveAudioBackend,AudioRecord}.cs` |
| Streaming service (server) | `src/dotnet/Streaming.Service/Services/{LiveAudioStreams,LiveStreamMuxer,ReplayStreamMuxer,AudioSegmentSaver}.cs` |
| Backend audio | `src/dotnet/Streaming.Service/Backend/{AudioStreamingBackend,LiveAudioBackend}.cs`, `AudioStreamingBackend.ProcessAudio.cs` |
| Transcribers | `src/dotnet/Streaming.Service/Services/Transcribers/` |
| Wire types | `src/dotnet/Api/Audio/{AudioFrame,AudioFormat,ActualOpusStream*}.cs`, `src/dotnet/Api/Live/Live*.cs` |
| Container converters | `src/dotnet/Api/Audio/{ActualOpus,Ogg,WebM}StreamConverter.cs`, `Api/Audio/Ogg/`, `Api/Audio/WebM/` |
| VAD | `src/dotnet/Core.Audio/{Onnx,Noop}VoiceActivityDetector.cs`, `…/AudioRecorder/workers/audio-vad*.ts`, `vad_batched.ort` |
| Constants | `src/dotnet/Api/Constants.Audio.cs`, `Api/AppConstants.Audio.cs` |
