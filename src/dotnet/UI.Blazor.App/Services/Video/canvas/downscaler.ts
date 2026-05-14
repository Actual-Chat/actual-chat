import { getLogs } from 'logging';
import { DeviceOrientation, type RotationQuarter } from 'orientation';
import type { DownscalerLike, LayerSpec } from '../operators/downscale';

const { infoLog, warnLog } = getLogs('VideoPipeline');

export interface CanvasDownscalerOptions {
    isFrontCamera?: boolean;
}

// Compositor-backed (GPU) downscaler via OffscreenCanvas 2D drawImage(VideoFrame).
// Layers are processed top-down so a lower tier can reuse an already-painted
// higher-tier canvas when longEdge(higher) >= 2 * longEdge(target).
//
// Rotation: when the source orientation no longer matches the locked encoder
// layers (e.g. Android Chrome flipped the track mid-stream), applies a
// quarter-turn canvas transform before drawImage so cropping stays in the
// original sensor space instead of taking a fresh "cover" crop from Chrome's
// rotated frame.
export class CanvasDownscaler implements DownscalerLike {
    private slots: Slot[] = [];
    private lastLoggedFitRotation: RotationQuarter | null = null;
    private lastLoggedInputState: FrameInputState | null = null;
    private initialDeviceAngle: number | null = null;

    constructor(private readonly opts: CanvasDownscalerOptions = {}) {}

    static pickSource(
        targetIdx: number,
        ladder: readonly LayerSpec[],
        paintedHigherIdxs: readonly number[],
    ): { kind: 'original' } | { kind: 'higher'; idx: number } {
        if (targetIdx >= ladder.length - 1)
            return { kind: 'original' };
        const targetLong = longEdge(ladder[targetIdx]);
        let bestIdx = -1;
        let bestLong = Number.POSITIVE_INFINITY;
        for (const i of paintedHigherIdxs) {
            if (i <= targetIdx) continue;
            const lo = longEdge(ladder[i]);
            if (lo >= 2 * targetLong && lo < bestLong) {
                bestIdx = i;
                bestLong = lo;
            }
        }
        return bestIdx >= 0 ? { kind: 'higher', idx: bestIdx } : { kind: 'original' };
    }

    // Async only to satisfy DownscalerLike — Canvas2D drawImage is synchronous.
    // eslint-disable-next-line @typescript-eslint/require-await
    async process(
        input: VideoFrame,
        layers: readonly LayerSpec[],
    ): Promise<VideoFrame[]> {
        const results = new Array<VideoFrame | null>(layers.length).fill(null);
        const painted: number[] = [];
        let mustCloseInput = true;
        try {
            this.ensureSlots(layers);
            const inputW = input.codedWidth;
            const inputH = input.codedHeight;
            this.logInputChange(input);
            this.initialDeviceAngle ??= DeviceOrientation.angle;
            const deviceAngle = DeviceOrientation.angle;
            const fitRotation = effectiveFitRotation(
                inputW, inputH,
                layers,
                this.initialDeviceAngle,
                deviceAngle,
                this.opts.isFrontCamera === true);
            if (fitRotation !== this.lastLoggedFitRotation) {
                infoLog?.log(
                    `CanvasDownscaler: cropboxRotation ${this.lastLoggedFitRotation ?? '(initial)'} -> ${fitRotation}`
                    + ` (source ${inputW}x${inputH}, encoder ${topLayer(layers).width}x${topLayer(layers).height}`
                    + `, deviceDelta=${formatDeviceDelta(this.initialDeviceAngle, deviceAngle)}`
                    + `, front=${this.opts.isFrontCamera === true})`);
                this.lastLoggedFitRotation = fitRotation;
            }
            // Source dims after the fit rotation — these are the dims the
            // cropper sees, since the rotation is baked into pixels via the
            // canvas transform.
            const rotatedSrcW = (fitRotation & 1) === 1 ? inputH : inputW;
            const rotatedSrcH = (fitRotation & 1) === 1 ? inputW : inputH;

            // Top-down: highest tier first so lower tiers can reuse it as source.
            for (let i = layers.length - 1; i >= 0; i--) {
                const { width, height } = layers[i];
                const slot = this.slots[i];
                if (slot.canvas.width !== width)
                    slot.canvas.width = width;
                if (slot.canvas.height !== height)
                    slot.canvas.height = height;

                const choice = CanvasDownscaler.pickSource(i, layers, painted);
                if (choice.kind === 'original') {
                    if (fitRotation === 0) {
                        const crop = computeCenterCrop(inputW, inputH, width, height);
                        slot.ctx.drawImage(
                            input,
                            crop.sx, crop.sy, crop.sw, crop.sh,
                            0, 0, width, height,
                        );
                    } else {
                        drawRotated(slot.ctx, input,
                            inputW, inputH, rotatedSrcW, rotatedSrcH,
                            width, height, fitRotation);
                    }
                } else {
                    // Higher-tier canvas is already aspect-corrected AND
                    // already rotated, so the lower tier just downscales it.
                    const srcSlot = this.slots[choice.idx];
                    slot.ctx.drawImage(
                        srcSlot.canvas,
                        0, 0, srcSlot.canvas.width, srcSlot.canvas.height,
                        0, 0, width, height,
                    );
                }
                results[i] = new VideoFrame(slot.canvas, {
                    timestamp: input.timestamp,
                    alpha: 'discard',
                });
                painted.push(i);
            }

            mustCloseInput = false;
            try { input.close(); } catch { /* already closed */ }
            return results as VideoFrame[];
        } catch (e) {
            warnLog?.log('CanvasDownscaler.process failed:', e);
            for (const r of results) {
                if (r) {
                    try { r.close(); } catch { /* ignore */ }
                }
            }
            if (mustCloseInput)
                try { input.close(); } catch { /* ignore */ }

            throw e;
        }
    }

    dispose(): void {
        this.slots = [];
    }

    // Private methods

    private ensureSlots(layers: readonly LayerSpec[]): void {
        while (this.slots.length < layers.length)
            this.slots.push(createSlot());
    }

    private logInputChange(input: VideoFrame): void {
        const next: FrameInputState = {
            rotation: readFrameRotationDeg(input),
            codedWidth: input.codedWidth,
            codedHeight: input.codedHeight,
            displayWidth: input.displayWidth,
            displayHeight: input.displayHeight,
        };
        const prev = this.lastLoggedInputState;
        if (prev !== null && sameFrameInputState(prev, next))
            return;

        warnLog?.log(
            `CanvasDownscaler input changed: prev=${prev === null ? '(initial)' : formatFrameInputState(prev)}`
            + ` new=${formatFrameInputState(next)}`);
        this.lastLoggedInputState = next;
    }
}

interface FrameInputState {
    rotation: number | null;
    codedWidth: number;
    codedHeight: number;
    displayWidth: number;
    displayHeight: number;
}

function sameFrameInputState(a: FrameInputState, b: FrameInputState): boolean {
    return a.rotation === b.rotation
        && a.codedWidth === b.codedWidth
        && a.codedHeight === b.codedHeight
        && a.displayWidth === b.displayWidth
        && a.displayHeight === b.displayHeight;
}

function formatFrameInputState(s: FrameInputState): string {
    return `{rotation=${s.rotation === null ? 'null' : s.rotation}, coded=${s.codedWidth}x${s.codedHeight}, display=${s.displayWidth}x${s.displayHeight}}`;
}

function readFrameRotationDeg(frame: VideoFrame): number | null {
    const raw = (frame as VideoFrame & { rotation?: number | null }).rotation;
    return typeof raw === 'number' && Number.isFinite(raw) ? raw : null;
}

interface Slot {
    canvas: OffscreenCanvas;
    ctx: OffscreenCanvasRenderingContext2D;
}

function createSlot(): Slot {
    const canvas = new OffscreenCanvas(0, 0);
    const ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });
    if (!ctx)
        throw new Error('CanvasDownscaler: 2D context unavailable on OffscreenCanvas');

    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    return { canvas, ctx };
}

function longEdge(spec: LayerSpec): number {
    return Math.max(spec.width, spec.height);
}

function topLayer(layers: readonly LayerSpec[]): LayerSpec {
    return layers[layers.length - 1];
}

function effectiveFitRotation(
    inputW: number,
    inputH: number,
    layers: readonly LayerSpec[],
    initialDeviceAngle: number,
    deviceAngle: number,
    isFrontCamera: boolean,
): RotationQuarter {
    if (layers.length === 0)
        return 0;

    const top = topLayer(layers);
    const sourceIsPortrait = inputH > inputW;
    const encoderIsPortrait = top.height > top.width;
    if (sourceIsPortrait === encoderIsPortrait)
        return 0;

    // A mismatch means Chrome has already rotated the MSTP frame relative to
    // the encoder orientation we locked at start. Rotate the cropbox in the
    // same direction as the phone moved from the initial downscale pose:
    // clockwise phone movement -> clockwise cropbox rotation, and vice versa.
    const deltaDeg = signedAngleDeltaDeg(initialDeviceAngle, deviceAngle)
        * (isFrontCamera ? 1 : -1);
    return deltaDeg < 0 ? 3 : 1;
}

function formatDeviceDelta(initialDeviceAngle: number, deviceAngle: number): string {
    return `${signedAngleDeltaDeg(initialDeviceAngle, deviceAngle)}deg (${initialDeviceAngle}->${deviceAngle})`;
}

function signedAngleDeltaDeg(from: number, to: number): number {
    if (!Number.isFinite(from) || !Number.isFinite(to))
        return 0;
    return ((((to - from + 180) % 360) + 360) % 360) - 180;
}

function computeCenterCrop(
    sx: number, sy: number,
    dx: number, dy: number,
): { sx: number; sy: number; sw: number; sh: number } {
    if (sx <= 0 || sy <= 0 || dx <= 0 || dy <= 0)
        return { sx: 0, sy: 0, sw: sx, sh: sy };

    const srcAspect = sx / sy;
    const dstAspect = dx / dy;
    if (Math.abs(srcAspect - dstAspect) < 1e-3)
        return { sx: 0, sy: 0, sw: sx, sh: sy };

    if (srcAspect > dstAspect) {
        const sw = Math.round(sy * dstAspect);
        return { sx: Math.floor((sx - sw) / 2), sy: 0, sw, sh: sy };
    }
    const sh = Math.round(sx / dstAspect);
    return { sx: 0, sy: Math.floor((sy - sh) / 2), sw: sx, sh };
}

// Draws `input` into the slot canvas with a quarter-turn CW transform of
// `rotation` applied to pixels. Crop is computed in rotated-source coords
// so it matches the canvas-space target aspect, then mapped back into the
// pre-rotation source rect that drawImage actually samples.
function drawRotated(
    ctx: OffscreenCanvasRenderingContext2D,
    input: VideoFrame,
    inputW: number, inputH: number,
    rotatedSrcW: number, rotatedSrcH: number,
    targetW: number, targetH: number,
    rotation: RotationQuarter,
): void {
    const cropR = computeCenterCrop(rotatedSrcW, rotatedSrcH, targetW, targetH);
    // Map the rotated-coords crop rect back into pre-rotation source coords.
    // For each quarter-turn CW, (x', y') = R^q (x_src - origin) where origin
    // shifts the rotated image back into the positive quadrant.
    let sx = 0, sy = 0, sw = 0, sh = 0;
    switch (rotation) {
    case 1: // 90° CW: rotated (x', y') = (inputH - 1 - y_src, x_src)
        sx = cropR.sy;
        sy = inputH - cropR.sx - cropR.sw;
        sw = cropR.sh;
        sh = cropR.sw;
        break;
    case 2: // 180°: rotated (x', y') = (inputW - 1 - x_src, inputH - 1 - y_src)
        sx = inputW - cropR.sx - cropR.sw;
        sy = inputH - cropR.sy - cropR.sh;
        sw = cropR.sw;
        sh = cropR.sh;
        break;
    case 3: // 270° CW (= 90° CCW): rotated (x', y') = (y_src, inputW - 1 - x_src)
        sx = inputW - cropR.sy - cropR.sh;
        sy = cropR.sx;
        sw = cropR.sh;
        sh = cropR.sw;
        break;
    default:
        sx = cropR.sx; sy = cropR.sy; sw = cropR.sw; sh = cropR.sh;
    }

    ctx.save();
    ctx.translate(targetW / 2, targetH / 2);
    ctx.rotate((rotation * Math.PI) / 2);
    // After translate+rotate, the canvas origin is at the canvas centre with
    // axes turned. To fill the canvas, draw at (-w/2, -h/2) with the *rotated*
    // target dims — for q=1,3 that's (-targetH/2, -targetW/2, targetH, targetW),
    // for q=2 it's (-targetW/2, -targetH/2, targetW, targetH).
    const swap = (rotation & 1) === 1;
    const dw = swap ? targetH : targetW;
    const dh = swap ? targetW : targetH;
    ctx.drawImage(input, sx, sy, sw, sh, -dw / 2, -dh / 2, dw, dh);
    ctx.restore();
}
