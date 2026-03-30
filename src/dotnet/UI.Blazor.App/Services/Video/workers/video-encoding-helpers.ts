/**
 * Video encoding helper functions.
 * Frame resize, YUV conversion, and H.264 codec description utilities.
 */

import { Log } from 'logging';

const { infoLog, warnLog } = Log.get('VideoPipeline');

/** Detect if description bytes are in avcC (H.264 decoder configuration record) format. */
export function isAvcCDescription(desc: ArrayBuffer): boolean {
    if (desc.byteLength < 5) return false;
    const bytes = new Uint8Array(desc);
    if (bytes[0] !== 0x01) return false;
    const validProfiles = [66, 77, 88, 100, 110, 122, 244];
    if (!validProfiles.includes(bytes[1])) return false;
    if ((bytes[4] & 0xFC) !== 0xFC) return false;
    return true;
}

/** Derive avc1 codec string from avcC description bytes. */
export function deriveAvcCodecFromDescription(desc: ArrayBuffer): string {
    const bytes = new Uint8Array(desc);
    const profile = bytes[1].toString(16).padStart(2, '0');
    const compat = bytes[2].toString(16).padStart(2, '0');
    const level = bytes[3].toString(16).padStart(2, '0');
    return `avc1.${profile}${compat}${level}`;
}

/**
 * Resize (and optionally rotate 90° CW) a frame to target dimensions.
 * Returns { frame, canvas, ctx } so caller can cache the canvas.
 *
 * @param rotate90 When true, applies 90° clockwise rotation before drawing.
 *   Used for iOS portrait where the camera sensor gives landscape frames (1280x720)
 *   but the encoder expects portrait (720x1280).
 */
export function resizeFrame(
    frame: VideoFrame,
    targetWidth: number,
    targetHeight: number,
    canvas: OffscreenCanvas | null,
    ctx: OffscreenCanvasRenderingContext2D | null,
    rotate90?: boolean,
): { frame: VideoFrame; canvas: OffscreenCanvas | null; ctx: OffscreenCanvasRenderingContext2D | null } {
    const frameWidth = frame.displayWidth;
    const frameHeight = frame.displayHeight;

    if (!rotate90 && frameWidth === targetWidth && frameHeight === targetHeight)
        return { frame, canvas, ctx };

    if (canvas?.width !== targetWidth || canvas.height !== targetHeight) {
        infoLog?.log(`Creating resize canvas: ${targetWidth}x${targetHeight}${rotate90 ? ' (with rotation)' : ''}`);
        canvas = new OffscreenCanvas(targetWidth, targetHeight);
        ctx = canvas.getContext('2d');
    }

    if (!ctx) {
        warnLog?.log('Could not create 2D context for resizing');
        return { frame, canvas, ctx };
    }

    ctx.fillStyle = '#000000';
    ctx.fillRect(0, 0, targetWidth, targetHeight);

    if (rotate90) {
        // Rotate 90° CW: in rotated coordinate space, drawing area is targetHeight × targetWidth
        // Letterbox the frame into that space to preserve aspect ratio
        const rotatedW = targetHeight;
        const rotatedH = targetWidth;
        const frameAspect = frameWidth / frameHeight;
        const rotatedAspect = rotatedW / rotatedH;
        let drawWidth: number, drawHeight: number, offsetX: number, offsetY: number;

        if (frameAspect > rotatedAspect) {
            drawWidth = rotatedW;
            drawHeight = rotatedW / frameAspect;
            offsetX = 0;
            offsetY = (rotatedH - drawHeight) / 2;
        } else {
            drawHeight = rotatedH;
            drawWidth = rotatedH * frameAspect;
            offsetX = (rotatedW - drawWidth) / 2;
            offsetY = 0;
        }

        ctx.save();
        ctx.translate(targetWidth, 0);
        ctx.rotate(Math.PI / 2);
        ctx.drawImage(frame, offsetX, offsetY, drawWidth, drawHeight);
        ctx.restore();
    } else {
        // Standard letterboxed resize
        const frameAspect = frameWidth / frameHeight;
        const targetAspect = targetWidth / targetHeight;
        let drawWidth: number, drawHeight: number, offsetX: number, offsetY: number;

        if (frameAspect > targetAspect) {
            drawWidth = targetWidth;
            drawHeight = targetWidth / frameAspect;
            offsetX = 0;
            offsetY = (targetHeight - drawHeight) / 2;
        } else {
            drawHeight = targetHeight;
            drawWidth = targetHeight * frameAspect;
            offsetX = (targetWidth - drawWidth) / 2;
            offsetY = 0;
        }

        ctx.drawImage(frame, offsetX, offsetY, drawWidth, drawHeight);
    }

    const newFrame = new VideoFrame(canvas, {
        timestamp: frame.timestamp,
        duration: frame.duration ?? undefined,
    });
    frame.close();
    return { frame: newFrame, canvas, ctx };
}

/**
 * CPU fallback: convert a non-YUV VideoFrame to I420 via BT.601 conversion.
 * Returns { frame, canvas, ctx } so caller can cache the canvas.
 */
export function cpuRgbaToI420(
    frame: VideoFrame,
    canvas: OffscreenCanvas | null,
    ctx: OffscreenCanvasRenderingContext2D | null,
): { frame: VideoFrame; canvas: OffscreenCanvas | null; ctx: OffscreenCanvasRenderingContext2D | null } {
    const w = frame.codedWidth;
    const h = frame.codedHeight;

    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
    if (canvas?.width !== w || canvas?.height !== h) {
        canvas = new OffscreenCanvas(w, h);
        ctx = canvas.getContext('2d', { willReadFrequently: true });
    }
    ctx!.drawImage(frame, 0, 0, w, h);
    const imageData = ctx!.getImageData(0, 0, w, h);
    const rgba = imageData.data;

    const chromaW = Math.ceil(w / 2);
    const chromaH = Math.ceil(h / 2);
    const ySize = w * h;
    const uvSize = chromaW * chromaH;
    const i420Buf = new ArrayBuffer(ySize + uvSize * 2);
    const out = new Uint8Array(i420Buf);

    for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
            const i = (y * w + x) * 4;
            const r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
            out[y * w + x] = Math.max(0, Math.min(255, Math.round(
                65.481 * r / 255 + 128.553 * g / 255 + 24.966 * b / 255 + 16
            )));
        }
    }

    for (let cy = 0; cy < chromaH; cy++) {
        for (let cx = 0; cx < chromaW; cx++) {
            let rSum = 0, gSum = 0, bSum = 0, count = 0;
            for (let dy = 0; dy < 2; dy++) {
                for (let dx = 0; dx < 2; dx++) {
                    const px = Math.min(cx * 2 + dx, w - 1);
                    const py = Math.min(cy * 2 + dy, h - 1);
                    const i = (py * w + px) * 4;
                    rSum += rgba[i]; gSum += rgba[i + 1]; bSum += rgba[i + 2]; count++;
                }
            }
            const r = rSum / count / 255, g = gSum / count / 255, b = bSum / count / 255;
            const chromaIdx = cy * chromaW + cx;
            out[ySize + chromaIdx] = Math.max(0, Math.min(255, Math.round(
                -37.797 * r - 74.203 * g + 112.0 * b + 128)));
            out[ySize + uvSize + chromaIdx] = Math.max(0, Math.min(255, Math.round(
                112.0 * r - 93.786 * g - 18.214 * b + 128)));
        }
    }

    const i420Frame = new VideoFrame(i420Buf, {
        format: 'I420',
        codedWidth: w,
        codedHeight: h,
        timestamp: frame.timestamp,
        duration: frame.duration ?? undefined,
        layout: [
            { offset: 0, stride: w },
            { offset: ySize, stride: chromaW },
            { offset: ySize + uvSize, stride: chromaW },
        ],
    });
    frame.close();
    return { frame: i420Frame, canvas, ctx };
}
