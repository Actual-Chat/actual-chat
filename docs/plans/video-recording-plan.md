# Video Recording & Streaming Architecture

## Overview

Video recording and streaming is implemented using a pipeline architecture built on WebCodecs, Web Workers, and SignalR. The system captures camera or screen input, processes it through an encode/decode pipeline with optional background blur, and streams encoded chunks to the server in real time.

## Current State

All core components are implemented and operational:

- Camera/screen capture with device selection
- H.264 / AV1 encoding via WebCodecs in dedicated workers
- Background blur via ONNX segmentation model (WebGPU or WASM)
- Network simulation for single-device testing
- Real-time server streaming via SignalR
- Remote playback with WebCodecs decoder
- Canvas fallbacks for Safari (no MediaStreamTrackProcessor/Generator)
- Adaptive quality stepping based on receiver latency
- Per-peer GOP skipping for slow connections

## Architecture

```
Camera/Screen → RecordingService → VideoPipeline {
    MediaStreamTrackProcessor (frame extraction, canvas fallback for Safari)
    → [optional SegmentationWorker for background blur]
    → EncoderWorker (WebCodecs H.264/AV1)
    → TransferSimulator (local testing) or VideoStreamer (SignalR → server)
    → DecoderWorker (WebCodecs, AV1 WASM fallback)
    → MediaStreamTrackGenerator (output stream, canvas fallback for Safari)
}
+ VideoStreamer (async: encoder chunks → SignalR PushVideo → StreamHub → LiveVideoBackend)
+ Separate preview stream → canvas rendering in VideoRecorder
```

### Two-Stream Design

The system uses separate streams for preview and pipeline output:
1. **Preview stream** — The raw camera MediaStream is rendered directly to a `<canvas>` in VideoRecorder for low-latency local preview.
2. **Pipeline output stream** — Frames flow through encode → transfer → decode to produce a processed MediaStream, used for recording and playback verification.

### Canvas Fallbacks

Safari lacks `MediaStreamTrackProcessor` and `MediaStreamTrackGenerator`. The pipeline uses canvas-based fallbacks:
- **Input**: A `<canvas>` + `requestAnimationFrame` loop draws video frames and extracts them manually.
- **Output**: Decoded frames are drawn to a canvas, and `captureStream()` produces the output MediaStream.

## Component Reference

### Core Services

| File | Purpose | Key API |
|------|---------|---------|
| [`services/video-pipeline.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/services/video-pipeline.ts) | Orchestrates the encode → transfer → decode pipeline using RPC workers | `start(inputStream)`, `stop()`, `reconfigure()`, `toggleBlur()`, `switchSegmentationBackend()`, `toggleAV1Decoder()` |
| [`services/recording-service.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/services/recording-service.ts) | High-level recording lifecycle: stream acquisition, config, state | `start()`, `stop()`, `toggleBlur()`, `updateSegmentationBackend()`, `getState()`, `getInputStream()`, `getOutputStream()` |
| [`video-streamer.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/video-streamer.ts) | SignalR-based real-time streaming of encoded chunks to server | `VideoStreamer.init(hubUrl)`, `VideoStreamer.addStream(token, chatId, config)`, `VideoStream.addFrame()` |

### Blazor Components

| File | Purpose |
|------|---------|
| [`VideoPanel.razor`](../../src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoPanel.razor) | Main video panel: recording controls, preview canvas, remote streams |
| [`VideoRecorder.razor`](../../src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoRecorder.razor) | Recording component with camera selection, blur toggle, quality directive subscription |
| [`VideoTrackPlayer.razor`](../../src/dotnet/UI.Blazor.App/Components/VideoPanel/VideoTrackPlayer.razor) | Plays a single remote video stream; registers viewer with backend |
| [`IVideoPlayerBackend.cs`](../../src/dotnet/UI.Blazor.App/Components/VideoPanel/IVideoPlayerBackend.cs) | Interface for JS→Blazor callbacks (`OnPlaying`, `OnEnded`) |
| [`JoinVideoCallModal.razor`](../../src/dotnet/UI.Blazor.App/Components/JoinVideoCallModal/JoinVideoCallModal.razor) | Camera/blur settings modal shown before joining |

### UI TypeScript

| File | Purpose | Key API |
|------|---------|---------|
| [`video-panel.ts`](../../src/dotnet/UI.Blazor.App/Components/VideoPanel/video-panel.ts) | UI chrome: expand/collapse, escape key | `VideoPanel.create()`, `startClosing()` |
| [`video-player.ts`](../../src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player.ts) | Remote video playback: SignalR pull, WebCodecs decode, latency reporting | `VideoPlayer.create()`, `startPull()`, `stop()` |
| [`video-recorder.ts`](../../src/dotnet/UI.Blazor.App/Components/VideoPanel/video-recorder.ts) | Recording controller: camera enumeration, preview rendering, blur toggle | `VideoRecorder.create()`, `startRecording()`, `stopRecording()`, `reconfigure()` |
| [`join-video-call-modal.ts`](../../src/dotnet/UI.Blazor.App/Components/JoinVideoCallModal/join-video-call-modal.ts) | Modal controller |

### State Services

| File | Purpose |
|------|---------|
| [`ChatVideoUI.cs`](../../src/dotnet/UI.Blazor.App/Services/ChatVideoUI.cs) | Video state orchestration per chat (compute methods, state mutators, JS callbacks) |
| [`ChatVideoUI.StateSync.cs`](../../src/dotnet/UI.Blazor.App/Services/ChatVideoUI.StateSync.cs) | Active speaker sync, auto-focus on streaming speaker (debounced 1.5s) |
| [`ChatVideoState.cs`](../../src/dotnet/UI.Blazor.App/Services/ChatVideoState.cs) | Immutable state record: ChatId, IsRecording, camera, blur, error |

### Workers

| File | Purpose | Key RPC Methods |
|------|---------|-----------------|
| [`workers/encoder-worker.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/workers/encoder-worker.ts) | WebCodecs video encoding (H.264/AV1) in dedicated thread | `initialize()`, `encodeFrame()`, `reconfigure()`, `stop()`, `getStats()` |
| [`workers/decoder-worker.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/workers/decoder-worker.ts) | WebCodecs video decoding with frame reordering and error recovery | `initialize()`, `decodeChunk()`, `stop()`, `toggleDecoderType()`, `getStats()` |
| [`workers/segmentation-worker.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/workers/segmentation-worker.ts) | ONNX-based person segmentation for background blur (WebGPU/WASM) | `initialize()`, `processFrame()`, `updateConfig()`, `stop()`, `getStats()` |

### Worker Contracts

| File | Purpose |
|------|---------|
| [`workers/encoder-worker-contract.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/workers/encoder-worker-contract.ts) | TypeScript interfaces for encoder worker RPC |
| [`workers/decoder-worker-contract.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/workers/decoder-worker-contract.ts) | TypeScript interfaces for decoder worker RPC |
| [`workers/segmentation-worker-contract.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/workers/segmentation-worker-contract.ts) | TypeScript interfaces for segmentation worker RPC |

### Codec & Encoding

| File | Purpose |
|------|---------|
| [`webcodecs-encoder.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/webcodecs-encoder.ts) | WebCodecs VideoEncoder wrapper with H.264/AV1 support |
| [`webcodecs-decoder.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/webcodecs-decoder.ts) | WebCodecs VideoDecoder wrapper with error recovery |
| [`codec-support.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/codec-support.ts) | Runtime codec detection with hardware acceleration probing |
| [`hevc-parser.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/hevc-parser.ts) | HEVC/H.265 bitstream parser |

### Network & Transfer

| File | Purpose |
|------|---------|
| [`utils/transfer-simulator.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/utils/transfer-simulator.ts) | Simulates network conditions (latency, jitter, packet loss, bandwidth) for local testing |
| [`utils/mp4-muxer.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/utils/mp4-muxer.ts) | MediaRecorder-based video muxing to WebM/MP4 for local recording |

Real network transfer uses [`video-streamer.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/video-streamer.ts) (SignalR with MessagePack).

### GPU & Segmentation Support

| File | Purpose |
|------|---------|
| [`webgpu-manager.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/webgpu-manager.ts) | WebGPU device/adapter management |
| [`webgpu-blur.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/webgpu-blur.ts) | WebGPU-based blur shader |
| [`gpu-support.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/gpu-support.ts) | GPU feature detection |
| [`tensor-utils.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/tensor-utils.ts) | Tensor manipulation utilities for segmentation model |

### Stats & Monitoring

| File | Purpose |
|------|---------|
| [`services/stats-service.ts`](../../src/dotnet/UI.Blazor.App/Services/Video/services/stats-service.ts) | Aggregates encoder/decoder/transfer/segmentation statistics |

### Styles

| File | Purpose |
|------|---------|
| [`video-panel.css`](../../src/dotnet/UI.Blazor.App/Components/VideoPanel/video-panel.css) | Video panel layout and styles |
| [`video-recorder.css`](../../src/dotnet/UI.Blazor.App/Components/VideoPanel/video-recorder.css) | Recorder preview and controls styles |
| [`join-video-call-modal.css`](../../src/dotnet/UI.Blazor.App/Components/JoinVideoCallModal/join-video-call-modal.css) | Modal styles with camera preview pattern |

## Technical Details

### Codec Configuration

Default codec is H.264 High profile (`avc1.640028`). AV1 is supported as an alternative.

| Parameter | Default |
|-----------|---------|
| Codec | H.264 High 4.0 (`avc1.640028`) |
| Resolution | 1280x720 |
| Bitrate | 2 Mbps |
| Frame rate | 30 fps |
| Latency mode | `realtime` |
| Hardware acceleration | `prefer-hardware` |
| Keyframe interval | Every 1 second (= frame rate) |

### Background Blur / Segmentation

The segmentation worker runs an ONNX person-segmentation model to produce a mask, which is used to blur the background. Supports:
- **WebGPU backend** — GPU-accelerated inference with zero-copy buffer tensors
- **WASM backend** — CPU fallback for browsers without WebGPU
- **Dynamic blur radius** — Configurable at runtime
- **Frame skipping** — Drops queued frames to maintain low latency under load

### Server Streaming

Encoded chunks are streamed to the server via SignalR with MessagePack serialization:
1. `VideoStreamer.init(hubUrl)` establishes the SignalR connection
2. `VideoStreamer.addStream(token, chatId, config)` creates a `VideoStream`
3. Each encoded chunk is added via `VideoStream.addFrame()` with timing metadata (offset in .NET ticks)
4. H.264 streams include SPS/PPS codec settings for decoder initialization
5. Server receives via `StreamHub.PushVideo()` → `LiveVideoBackend.PushVideo()` → `StreamStore<VideoFrame>`

### Server Receiving (Playback Pull)

Receivers pull video from the server via SignalR:
1. `VideoPlayer.startPull(streamId, skipToMs)` calls `StreamHub.GetVideo()` which returns `IAsyncEnumerable<byte[][]>` batches
2. `LiveVideoBackend.GetVideo(streamId, skipTo, peerId)` retrieves frames from `StreamStore`, applying keyframe seeking and per-peer GOP skipping
3. JS `VideoPlayer` decodes frames via WebCodecs `VideoDecoder` and renders to canvas
4. Latency is measured and reported every 5s via `StreamHub.ReportVideoLatency()`
5. Backend evaluates latency across all peers and may adjust sender quality (see adaptive quality in `video-streaming-plan.md`)

### RPC Worker Communication

Workers communicate with the main thread using a custom RPC framework (`rpc.ts`):
- `rpcClient()` — Creates a typed proxy for calling worker methods
- `rpcServer()` — Sets up a message handler inside the worker
- `rpcClientServer()` — Bidirectional communication (main ↔ worker)
- Supports transferable objects (`VideoFrame`, `MessagePort`) for zero-copy transfer

## Future Enhancements

- SFU-based multi-participant video routing
- Full adaptive bitrate with bandwidth estimation (quality stepping and GOP skipping are already implemented)
- Additional segmentation models (virtual backgrounds, face tracking)
- Recording to cloud storage
- Picture-in-picture support
