# Live Video Pipeline — Documentation Index

This folder documents the **current** live-video pipeline end-to-end, from a
camera frame in browser A to the pixels rendered in browser B. It is written
from current source code under `src/`.

## Reading order

| # | Doc | Scope |
|---|-----|-------|
| 1 | [01-end-to-end.md](./01-end-to-end.md) | Browser → server → browser walkthrough; one diagram per stage |
| 2 | [02-sender.md](./02-sender.md) | Sender pipeline: capture → operators → encode → wire-send |
| 3 | [03-codecs-and-layers.md](./03-codecs-and-layers.md) | Codec detection, hardware acceleration, simulcast ladder, keyframe policy |
| 4 | [04-rpc-and-framing.md](./04-rpc-and-framing.md) | `VideoFrame` DTO, `RpcStream` flow control, `VideoFrameDto` ↔ `VideoStreamFrame` |
| 5 | [05-server-publish.md](./05-server-publish.md) | `PushStream` → `VideoStreamingBackend` → `VideoStreamMemoizer` |
| 6 | [06-server-fanout.md](./06-server-fanout.md) | `LiveVideoBackend` chat registry, codec negotiation, cross-shard `RemoteStreamCaches` |
| 7 | [07-receiver.md](./07-receiver.md) | Receiver pipeline: pull → epoch-reset → paced buffer → decode → present |
| 8 | [08-quality-control.md](./08-quality-control.md) | Sender AIMD, receiver verdicts, `ReceiveQualityFilter`, keyframe requests |
| 9 | [09-diagnostics.md](./09-diagnostics.md) | Meters, diagnostics modals, tunable constants |
| 10 | [10-glossary.md](./10-glossary.md) | Glossary of types, files, and abbreviations |
| 11 | [11-buffering-and-av-sync.md](./11-buffering-and-av-sync.md) | Original "one buffer per side" goal vs. current shape; A/V sync (wired but disabled) |

## Top-level architecture

```mermaid
flowchart LR
    subgraph BrowserA["Sender browser"]
        CamA[Camera /<br/>Display Capture]
        WorkerA[recorderWorker.js<br/>capture → encode → wire]
        PreviewA[Local preview<br/>&lt;video&gt; (MSTG)]
    end

    subgraph API["API pod (any node)"]
        LVS[ILiveVideoStreams<br/>PushStream / GetStream<br/>ChangePlaybackQuality]
        RVC[RemoteVideoStreamCache]
    end

    subgraph Backend["Backend pod (sharded by ChatId)"]
        VSB[VideoStreamingBackend<br/>ProcessFrames]
        Memo[VideoStreamMemoizer<br/>~3.3 s tail]
        LVB[LiveVideoBackend<br/>chat & member registry]
        Redis[(Redis<br/>streams + members)]
    end

    subgraph BrowserB["Receiver browser"]
        WorkerB[playerWorker.js<br/>pull → buffer → decode]
        DOMB[&lt;video&gt; via MSTG<br/>or canvas]
    end

    CamA --> WorkerA
    WorkerA -.-> PreviewA
    WorkerA -- "RpcStream&lt;VideoFrame&gt;<br/>(realtime, no resume)" --> LVS
    LVS --> VSB
    VSB --> Memo
    VSB <--> LVB
    LVB <--> Redis
    Memo --> LVS
    LVS -.->|cross-shard fetch| RVC
    RVC -.-> Memo
    LVS -- "RpcStream&lt;VideoFrame&gt;<br/>(per-consumer filtered)" --> WorkerB
    WorkerB --> DOMB
    WorkerB -- "ChangePlaybackQuality<br/>(per-stream ReceiveQuality)" --> LVS
    WorkerA -- "ChangeRecordingQuality<br/>(encoder health)" --> LVS
```

## Cheat-sheet: where things live

| Concern | Location |
|---|---|
| Sender TS pipeline | `src/dotnet/UI.Blazor.App/Services/Video/sender/`, `…/operators/` |
| Receiver TS pipeline | `src/dotnet/UI.Blazor.App/Services/Video/playback/`, `…/operators/` |
| Codec / GPU support | `src/dotnet/UI.Blazor.App/Services/Video/codec-support.ts`, `gpu-support.ts`, `hevc-codec-selection.ts` |
| Simulcast ladder | `src/dotnet/UI.Blazor.App/Components/VideoPanel/layer-ladder.ts` |
| TS↔C# RPC glue | `src/dotnet/UI.Blazor.App/Services/Video/streaming/streaming-glue.ts`, `streaming-rpc-client.ts` |
| Razor entry components | `src/dotnet/UI.Blazor.App/Components/VideoPanel/{VideoTrackPlayer,RemoteStreamPlayer,VideoStreamingPreview}.razor` |
| Quality controller (C#) | `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs` |
| RPC contracts | `src/dotnet/Api.Contracts/Streaming/ILiveVideoStreams.cs`, `…/Quality/*` |
| Streaming service | `src/dotnet/Streaming.Service/Services/{LiveVideoStreams,ReceiveQualityFilter,StreamStore,RemoteStreamCaches}.cs` |
| Backend service | `src/dotnet/Streaming.Service/Backend/{VideoStreamingBackend,VideoStreamMemoizer,LiveVideoBackend*}.cs` |
| `VideoFrame` / `VideoFormat` | `src/dotnet/Api/Video/` |
| Constants | `src/dotnet/Api/Constants.Video.cs` |

## Three-line summary

The sender runs a Web Worker that captures `VideoFrame`s, downscales them on
the GPU into a 1–3 layer simulcast ladder, encodes each layer with WebCodecs,
and pushes a single MessagePack-serialized `VideoFrame` stream to the API pod.
The backend memoizes ~3.3 s of frames per layer and fans them out — to viewers
on the same node directly, to viewers on other nodes through a cross-shard
cache. Each viewer reads through `ReceiveQualityFilter`, which gates layers and
temporal layers based on the latest `ChangePlaybackQuality` call from that
viewer's worker.
