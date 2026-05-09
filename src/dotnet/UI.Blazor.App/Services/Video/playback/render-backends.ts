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
