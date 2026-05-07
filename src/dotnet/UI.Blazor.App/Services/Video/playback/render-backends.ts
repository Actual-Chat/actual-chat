import type { CanvasImageInterface } from '../operators/present-canvas';

/**
 * Tag identifying which present sink the playback pipeline should
 * terminate in. The two production backends are:
 *  - `'mstg'` — write decoded frames to a `MediaStreamTrackGenerator`
 *    `WritableStream<VideoFrame>` whose `track` is fed to a `<video>`
 *    via `srcObject`. Zero-copy, hardware-overlay-friendly path,
 *    available on Chromium and Firefox.
 *  - `'canvas'` — `drawImage` each frame onto a 2D context (offscreen
 *    or main-thread). Used on Safari, where MSTG is unavailable, and
 *    for diagnostics overlays.
 */
export type RenderBackendKind = 'mstg' | 'canvas';

/**
 * Producer-side configuration for the MSTG backend. The writer is
 * provided by the platform layer (the worker that owns the
 * `MediaStreamTrackGenerator` instance) and passed in here so the
 * playback module stays free of platform-specific construction.
 */
export interface MstgBackendConfig {
    kind: 'mstg';
    writer: WritableStreamDefaultWriter<VideoFrame>;
}

/**
 * Configuration for the canvas backend. `convertToBitmap` is required
 * only on Safari, where `ctx.drawImage(VideoFrame, …)` is unsupported
 * and the sink must promote to an `ImageBitmap` first.
 */
export interface CanvasBackendConfig {
    kind: 'canvas';
    canvasCtx: CanvasImageInterface;
    convertToBitmap?: (frame: VideoFrame) => Promise<ImageBitmap>;
}

export type RenderBackendConfig = MstgBackendConfig | CanvasBackendConfig;

/**
 * Pure helper for backend selection. Given the caller's preference
 * and the resources it has on hand, return one concrete config.
 *
 * Rules:
 *  - `preferMstg` + a writer supplied → MSTG.
 *  - Otherwise, if a canvas context is supplied → canvas (Safari path).
 *  - Otherwise, throw — the caller must provide at least one usable
 *    rendering surface. We intentionally don't have a "headless" mode;
 *    the playback pipeline always terminates in a real present sink.
 *
 * The caller's job is to populate the optional fields with whatever
 * the platform provides. Production wires both: MSTG writer when
 * `MediaStreamTrackGenerator` is in `globalThis`, canvas context
 * always (as a fallback). Tests pass exactly one of the two to assert
 * the selection branches.
 */
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
