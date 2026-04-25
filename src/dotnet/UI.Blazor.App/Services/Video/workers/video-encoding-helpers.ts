/**
 * Video encoding helper functions.
 * Frame resize, YUV conversion, and H.264 codec description utilities.
 */

import { getLogs } from 'logging';

const { infoLog, warnLog } = getLogs('VideoPipeline');

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

// AVC level byte for a given coded area, per H.264 Annex A Table A-1. Picks the
// smallest level that admits the resolution. Used to bump the description-derived
// level when the encoder reports a level only sufficient for the BASE tier but
// the simulcast ladder needs higher (extras up to 1080p / 4K). Without this
// the avcC fallback path bakes Level 3.0 (max 720×576) into the codec string,
// then the 1280×720 extra encoder rejects with NotSupportedError.
export function pickAvcLevelByte(width: number, height: number): number {
    const pixels = width * height;
    if (pixels > 2_073_600) return 0x34; // Level 5.2 — above 1080p area (4K tiers and beyond)
    if (pixels > 921_600)   return 0x28; // Level 4.0 — above 720p area, up to 1080p area
    return 0x1F;                         // Level 3.1 — up to 720p area (≤ 921,600)
}

// Derive avc1 codec string from avcC description bytes. `minLevelByte` raises
// the level if the description's level is below it — caller passes the level
// required by the *largest* simulcast tier so all extras inherit a string that
// admits their resolution.
export function deriveAvcCodecFromDescription(desc: ArrayBuffer, minLevelByte = 0): string {
    const bytes = new Uint8Array(desc);
    const profile = bytes[1].toString(16).padStart(2, '0');
    const compat = bytes[2].toString(16).padStart(2, '0');
    const descLevel = bytes[3];
    const finalLevel = Math.max(descLevel, minLevelByte).toString(16).padStart(2, '0');
    return `avc1.${profile}${compat}${finalLevel}`;
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
    // When rotating, use coded dimensions (actual pixel layout) rather than display dimensions.
    // Safari may report portrait display dims while pixel data is still in landscape sensor orientation.
    const frameWidth = rotate90 ? (frame.codedWidth || frame.displayWidth) : frame.displayWidth;
    const frameHeight = rotate90 ? (frame.codedHeight || frame.displayHeight) : frame.displayHeight;

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

    if (rotate90) {
        // Rotate 90° CW with center-crop: source is landscape, target is portrait
        // In rotated coordinate space, drawing area is targetHeight × targetWidth
        const rotatedW = targetHeight;
        const rotatedH = targetWidth;
        const frameAspect = frameWidth / frameHeight;
        const rotatedAspect = rotatedW / rotatedH;

        // Center-crop source to match rotated target aspect ratio
        let srcX = 0, srcY = 0, srcW = frameWidth, srcH = frameHeight;
        if (frameAspect > rotatedAspect) {
            srcW = Math.round(frameHeight * rotatedAspect);
            srcX = Math.round((frameWidth - srcW) / 2);
        } else if (frameAspect < rotatedAspect) {
            srcH = Math.round(frameWidth / rotatedAspect);
            srcY = Math.round((frameHeight - srcH) / 2);
        }

        ctx.save();
        ctx.translate(targetWidth, 0);
        ctx.rotate(Math.PI / 2);
        ctx.drawImage(frame, srcX, srcY, srcW, srcH, 0, 0, rotatedW, rotatedH);
        ctx.restore();
    } else {
        // Center-crop resize: crop source to match target aspect ratio, then scale
        const frameAspect = frameWidth / frameHeight;
        const targetAspect = targetWidth / targetHeight;

        let srcX = 0, srcY = 0, srcW = frameWidth, srcH = frameHeight;
        if (frameAspect > targetAspect) {
            srcW = Math.round(frameHeight * targetAspect);
            srcX = Math.round((frameWidth - srcW) / 2);
        } else if (frameAspect < targetAspect) {
            srcH = Math.round(frameWidth / targetAspect);
            srcY = Math.round((frameHeight - srcH) / 2);
        }

        ctx.drawImage(frame, srcX, srcY, srcW, srcH, 0, 0, targetWidth, targetHeight);
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
