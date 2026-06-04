import type { CanvasImageInterface } from '../operators/present-canvas';

// 'mstg' — write decoded frames to a MediaStreamTrackGenerator's
// WritableStream<VideoFrame> whose track feeds <video srcObject>.
// Zero-copy, hardware-overlay-friendly; Chromium and Firefox.
// 'canvas' — drawImage each frame onto a 2D context. Used on Safari
// (no MSTG) and for diagnostics overlays.
export type RenderBackendKind = 'mstg' | 'canvas';

export interface MstgBackendConfig {
    kind: 'mstg';
    writer: WritableStreamDefaultWriter<VideoFrame>;
}

export interface CanvasBackendConfig {
    kind: 'canvas';
    canvasCtx: CanvasImageInterface;
    // Required only on Safari, where ctx.drawImage(VideoFrame, ...)
    // is unsupported and the sink must promote to ImageBitmap first.
    convertToBitmap?: (frame: VideoFrame) => Promise<ImageBitmap>;
}

export type RenderBackendConfig = MstgBackendConfig | CanvasBackendConfig;

// Rules:
//  - preferMstg + a writer supplied → MSTG.
//  - Otherwise, if a canvas context is supplied → canvas (Safari path).
//  - Otherwise, throw — the playback pipeline always terminates in a
//    real present sink (no headless mode).
export function pickRenderBackend(opts: {
    preferMstg: boolean;
    canvasCtx?: CanvasImageInterface;
    mstgWriter?: WritableStreamDefaultWriter<VideoFrame>;
    convertToBitmap?: (frame: VideoFrame) => Promise<ImageBitmap>;
}): RenderBackendConfig {
    if (opts.preferMstg && opts.mstgWriter) {
        return { kind: 'mstg', writer: opts.mstgWriter };
    }
    if (opts.canvasCtx) {
        return {
            kind: 'canvas',
            canvasCtx: opts.canvasCtx,
            convertToBitmap: opts.convertToBitmap,
        };
    }
    throw new Error(
        'pickRenderBackend: no rendering surface available '
        + '(neither MSTG writer nor canvas context supplied)');
}

// A worker-scope video track generator: a WritableStream<VideoFrame> sink
// whose `track` can be transferred to the main thread and attached to a
// <video srcObject>.
export interface WorkerVideoTrackGenerator {
    writable: WritableStream<VideoFrame>;
    track: MediaStreamTrack;
}

interface VideoTrackGeneratorGlobal {
    MediaStreamTrackGenerator?: new (init: { kind: 'video' }) => MediaStreamTrack & {
        readonly writable: WritableStream<VideoFrame>;
    };
    // Safari-only equivalent: same shape but a different ctor name and the
    // emitted MediaStreamTrack lives on `.track`, not on the generator itself.
    VideoTrackGenerator?: new () => {
        readonly writable: WritableStream<VideoFrame>;
        readonly track: MediaStreamTrack;
    };
}

// Constructs a video track generator from whichever ctor the current realm
// exposes — MediaStreamTrackGenerator (Chromium) or VideoTrackGenerator
// (Safari). Both are worker-only on Safari, so this MUST be called from a
// worker realm there. Returns null when neither ctor is available; the caller
// should fall back to canvas. Shared by the player and recorder workers.
export function createWorkerVideoTrackGenerator(): WorkerVideoTrackGenerator | null {
    const g = globalThis as unknown as VideoTrackGeneratorGlobal;
    if (typeof g.MediaStreamTrackGenerator === 'function') {
        const generator = new g.MediaStreamTrackGenerator({ kind: 'video' });
        return { writable: generator.writable, track: generator };
    }
    if (typeof g.VideoTrackGenerator === 'function') {
        const generator = new g.VideoTrackGenerator();
        return { writable: generator.writable, track: generator.track };
    }
    return null;
}
