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
    getOutputSize(): { width: number; height: number } | null;
    drawFrame(pf: PresentableFrame): void;
    // Quarter-turn (0..3, CW from natural) to apply at display time. The
    // backend writes the CSS transform on its inner element and adjusts
    // the parent's aspect-ratio (swapping W/H when the quarter is odd).
    setRotation(quarter: number): void;
    // Re-applies rotation-dependent layout. Odd-quarter rotations require
    // the inner element to be sized to the parent's transposed dimensions,
    // so VideoPlayer calls this on every parent resize.
    recomputeLayout(): void;
    // Selects how the inner element fits the tile: cover (crop to fill)
    // or contain (letterbox to show the whole frame). VideoPlayer drives
    // this from the cover-vs-contain loss metric.
    setFit(fit: 'cover' | 'contain'): void;
    // Optional .remote-video-bg canvas + a hint of whether this tile is
    // the focused one. The backend pumps the blurred backdrop only when
    // both `fit === 'contain'` AND `focused === true`.
    setBackdrop(canvas: HTMLCanvasElement | null, focused: boolean): void;
    // Tells the backend that QC has paused this stream (no frames will arrive
    // for a while). Used to suppress stall watchdogs that would otherwise
    // mistake intentional silence for a real playback freeze.
    setExpectedPaused(paused: boolean): void;
    dispose(): void;
}
