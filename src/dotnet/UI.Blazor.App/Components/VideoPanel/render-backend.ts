// Render backend abstraction for VideoPlayer. Two impls today:
//   - CanvasRenderBackend: paints to a 2D canvas (legacy, all browsers)
//   - MstgRenderBackend (TODO): writes VideoFrames to a MediaStreamTrackGenerator
//     and lets a <video srcObject> render natively (Safari 18.4+, Chromium).
// VideoPlayer drives selection (audio-clock filter, jitter buffer) and hands one
// frame at a time via drawFrame(). Backend owns presentation only.

export interface PresentableFrame {
    readonly drawable: VideoFrame | ImageBitmap;
    readonly timestamp: number;
    readonly displayWidth: number;
    readonly displayHeight: number;
}

export type RenderBackendKind = 'canvas' | 'mstg';

export interface RenderBackend {
    readonly kind: RenderBackendKind;
    // When true, VideoPlayer must NOT feed frames via drawFrame() and must NOT
    // run a main-thread render loop — the backend pulls frames in the worker.
    readonly isOffThread: boolean;
    drawFrame(pf: PresentableFrame): void;
    dispose(): void;
}
