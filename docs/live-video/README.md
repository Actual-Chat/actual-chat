# Live Video Pipeline — Documentation Index

This folder documents the **current** live-video pipeline end-to-end, from a
camera frame in browser A to the pixels rendered in browser B. It is written
from current source code under `src/`.

## Reading order

| # | Doc | Scope |
|---|-----|-------|
| 1 | [01-end-to-end.md](./01-end-to-end.md) | Browser → server → browser walkthrough; one diagram per stage |
| 2 | [02-sender.md](./02-sender.md) | Sender pipeline: capture → flood-gate → downscale → encode → bundle → wire |
| 3 | [03-codecs-and-layers.md](./03-codecs-and-layers.md) | Codec detection, hardware acceleration, simulcast ladder, keyframe policy |
| 4 | [04-rpc-and-framing.md](./04-rpc-and-framing.md) | `VideoFrameBundle` on PushStream, `VideoFrame` on GetStream, `RpcStream` flow control |
| 5 | [05-server-publish.md](./05-server-publish.md) | `PushStream` → `VideoStreamingBackend` → `VideoStreamMemoizer`, silence watchdog |
| 6 | [06-server-fanout.md](./06-server-fanout.md) | `LiveVideoBackend` chat registry, codec negotiation, deduped cross-shard `RemoteVideoStreamCache` |
| 7 | [07-receiver.md](./07-receiver.md) | Receiver pipeline: pull → epoch-reset → span-gated buffer → decode → present |
| 8 | [08-quality-control.md](./08-quality-control.md) | Sender AIMD, receiver verdicts, `ReceiveQualityFilter`, keyframe-on-upgrade |
| 9 | [09-diagnostics.md](./09-diagnostics.md) | Meters, diagnostics modal, debug overrides, tunable constants |
| 10 | [10-glossary.md](./10-glossary.md) | Glossary of types, files, and abbreviations |
| 11 | [11-buffering-and-av-sync.md](./11-buffering-and-av-sync.md) | "One buffer per side" goal vs. current shape; A/V sync (wired but disabled) |
| — | [codec-performance.md](./codec-performance.md) | Measured encode cost and codec support per device — the evidence behind the encoder ladder |

## Top-level architecture

```mermaid
flowchart LR
    subgraph BrowserA["Sender browser"]
        CamA[Camera /<br/>Display Capture]
        WorkerA[recorderWorker.js<br/>capture → encode → bundle → wire]
        PreviewA[Local preview<br/>&lt;video&gt; (MSTG)]
    end

    subgraph API["API pod (any node)"]
        LVS[ILiveVideoStreams<br/>PushStream / GetStream<br/>ChangePlaybackQuality]
        RVC[RemoteVideoStreamCache<br/>(deduped fetch)]
    end

    subgraph Backend["Backend pod (sharded by ChatId)"]
        VSB[VideoStreamingBackend<br/>ProcessFrames<br/>(decompose bundle → frames)]
        Memo[VideoStreamMemoizer<br/>~3.3 s tail per layer]
        LVB[LiveVideoBackend<br/>chat & member registry]
        Redis[(Redis<br/>streams + members)]
    end

    subgraph BrowserB["Receiver browser"]
        WorkerB[playerWorker.js<br/>pull → buffer → decode]
        DOMB[&lt;video&gt; via MSTG<br/>or canvas]
    end

    CamA --> WorkerA
    WorkerA -.-> PreviewA
    WorkerA -- "RpcStream&lt;VideoFrameBundle&gt;<br/>(realtime, canSkipTo=isKeyFrame)" --> LVS
    LVS --> VSB
    VSB --> Memo
    VSB <--> LVB
    LVB <--> Redis
    Memo --> LVS
    LVS -.->|cross-shard fetch (deduped)| RVC
    RVC -.-> Memo
    LVS -- "RpcStream&lt;VideoFrame&gt;<br/>(per-consumer ReceiveQualityFilter)" --> WorkerB
    WorkerB --> DOMB
    WorkerB -- "ChangePlaybackQuality<br/>(per-stream ReceiveQuality + render-size hint)" --> LVS
    WorkerA -- "ChangeRecordingQuality<br/>(encoder health, 1 Hz)" --> LVS
```

## Cheat-sheet: where things live

| Concern | Location |
|---|---|
| Sender TS pipeline | `src/dotnet/UI.Blazor.App/Services/Video/sender/`, `…/operators/` |
| Receiver TS pipeline | `src/dotnet/UI.Blazor.App/Services/Video/playback/`, `…/operators/` |
| Push→pull bridge (sender) | `src/dotnet/UI.Blazor.App/Services/Video/streaming/push-to-pull-buffer.ts` |
| Codec / GPU support | `src/dotnet/UI.Blazor.App/Services/Video/codec-support.ts`, `support/gpu.ts`, `hevc-codec-selection.ts` |
| Downscalers | `src/dotnet/UI.Blazor.App/Services/Video/canvas/downscaler.ts` (`CanvasDownscaler`), `webgpu/downscaler.ts` |
| Simulcast ladder | `src/dotnet/UI.Blazor.App/Components/VideoPanel/layer-ladder.ts` |
| Razor entry components | `src/dotnet/UI.Blazor.App/Components/VideoPanel/{VideoTrackPlayer,VideoStreamingPreview}.razor` |
| Quality controller (C#) | `src/dotnet/UI.Blazor.App/Services/VideoQualityUI.cs` |
| RPC contracts | `src/dotnet/Api.Contracts/Streaming/ILiveVideoStreams.cs`, `…/Quality/*` |
| Streaming service | `src/dotnet/Streaming.Service/Services/{LiveVideoStreams,ReceiveQualityFilter,StreamStore,RemoteStreamCaches,StreamSilenceWatchdog}.cs` |
| Backend service | `src/dotnet/Streaming.Service/Backend/{VideoStreamingBackend,VideoStreamMemoizer,LiveVideoBackend*}.cs` |
| `VideoFrame` / `VideoFrameBundle` / `VideoFormat` | `src/dotnet/Api/Video/` |
| Constants (.NET) | `src/dotnet/Api/Constants.Video.cs` |
| Constants (TS, derived) | `src/nodejs/src/app-constants.ts` (`expandVideo`) |

## Three-line summary

The sender runs a Web Worker that captures `VideoFrame`s, downscales them on
the GPU (or a 2D canvas) into a 1–3 layer simulcast ladder, encodes each layer
with WebCodecs, packs all per-source-moment layers into a single
`VideoFrameBundle`, and pushes that bundle stream to the API pod. The backend
decomposes each bundle into per-layer `VideoFrame`s, memoizes ~3.3 s of frames
per layer, and fans them out — to viewers on the same node directly, to
viewers on other nodes through a deduped cross-shard cache. Each viewer reads
through `ReceiveQualityFilter`, which gates spatial and temporal layers based
on the latest `ChangePlaybackQuality` call from that viewer and clamps to the
producer's currently advertised `MaxLayerId`.
