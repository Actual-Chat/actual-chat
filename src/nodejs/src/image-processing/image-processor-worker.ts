import { rpcServer } from 'rpc';
import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { base64Encode } from '../actuallab-rpc/base64.js';
import { HeifDecoder } from './heif-decoder';
import { canHaveAlpha, chooseEncoding, tryKeepSource } from './image-encoding-policy';
import { getImageMimeType, isAnimatedImage, readImageDimensions, sniffImageFormat } from './image-format';
import { fitWithinBudget } from './image-geometry';
import { JpegliEncoder } from './jpegli-encoder';
import { stripImageMetadata } from './metadata-stripper';
import { encodePlaceholder } from './placeholder-encoder';
import type {
    ImageFormat,
    ImageOutput,
    ImageOutputSpec,
    ImageProcessorWorker,
    ImageProcessRequest,
    ImageProcessResult,
} from './image-processing-contracts';

const { debugLog, errorLog } = getLogs('ImageProcessorWorker');

const JPEG_DISTANCE = 1.9;
// WebKit's canvas encoder maps quality much higher than Chromium's; both land near SSIMULACRA2 74
const FALLBACK_JPEG_QUALITY = DeviceInfo.isWebKit ? 0.5 : 0.75;

let distBaseUrl = '';
let whenEncoderLoaded: Promise<JpegliEncoder | null> | null = null;
let whenDecoderLoaded: Promise<HeifDecoder | null> | null = null;
let queueTail: Promise<unknown> = Promise.resolve();

export const serverImpl: ImageProcessorWorker = {
    init: (baseUrl: string): Promise<void> => {
        distBaseUrl = baseUrl.replace(/\/$/, '');
        return Promise.resolve();
    },
    // One image at a time: a decoded 4K frame alone is ~44 MB
    process: (source: Blob, request: ImageProcessRequest): Promise<ImageProcessResult> =>
        enqueue(() => processImage(source, request)),
};

rpcServer('ImageProcessorWorker.server', self as unknown as Worker, serverImpl);

function enqueue<T>(run: () => Promise<T>): Promise<T> {
    const result = queueTail.then(run, run);
    queueTail = result.catch(() => undefined);
    return result;
}

async function processImage(source: Blob, request: ImageProcessRequest): Promise<ImageProcessResult> {
    const startedAt = performance.now();
    const bytes = new Uint8Array(await source.arrayBuffer());
    const format = sniffImageFormat(bytes);
    const isAnimated = isAnimatedImage(bytes, format);
    const dimensions = readImageDimensions(bytes, format);
    const width = dimensions?.width ?? 0;
    const height = dimensions?.height ?? 0;
    const outputs: ImageOutput[] = [];
    let bitmap: ImageBitmap | null = null;
    try {
        for (const spec of request.outputs) {
            if (spec.kind === 'placeholder') {
                outputs.push(await createPlaceholderOutput(source, bytes, format, bitmap));
                continue;
            }

            const isOversized = spec.maxPassthroughPixels !== null && width * height > spec.maxPassthroughPixels;
            if (chooseEncoding(format, isAnimated, spec.codec, isOversized) === 'passthrough') {
                outputs.push(createPassthroughOutput(source, bytes, format, spec));
                continue;
            }

            // Checked from the header's dimensions, before the decode it exists to avoid;
            // reencode() repeats it from the decoded bitmap for a source whose header has none.
            if (dimensions) {
                const target = fitWithinBudget(dimensions.width, dimensions.height, spec.maxPixels, spec.maxLongSide);
                if (!canEncodeOnThisDevice(target.width * target.height, request.maxMobileEncodePixels)) {
                    outputs.push({ ...createPassthroughOutput(source, bytes, format, spec), declined: true });
                    continue;
                }
            }

            bitmap ??= await decodeBitmap(source, bytes, format);
            outputs.push(
                await reencode(bitmap, source, bytes, format, spec, request.maxMobileEncodePixels));
        }
        const elapsedMs = Math.round(performance.now() - startedAt);
        debugLog?.log(`processImage: ${format}, ${outputs.length} output(s) in ${elapsedMs}ms`);
        return { format, outputs };
    }
    finally {
        bitmap?.close();
    }
}

async function createPlaceholderOutput(
    source: Blob,
    bytes: Uint8Array,
    format: ImageFormat,
    bitmap: ImageBitmap | null,
): Promise<ImageOutput> {
    const placeholderBitmap = bitmap ?? await tryCreatePlaceholderBitmap(source, bytes, format);
    try {
        const packed = placeholderBitmap
            ? await tryEncodeWithRebuild(getEncoder, encoder => encodePlaceholder(placeholderBitmap, encoder))
            : null;
        return {
            kind: 'placeholder', blob: new Blob(), mimeType: '', width: 0, height: 0, isSource: false,
            placeholder: packed ? base64Encode(packed) : '',
        };
    }
    finally {
        if (placeholderBitmap && placeholderBitmap !== bitmap)
            placeholderBitmap.close();
    }
}

async function tryCreatePlaceholderBitmap(
    source: Blob,
    bytes: Uint8Array,
    format: ImageFormat,
): Promise<ImageBitmap | null> {
    // Runs only when the main output left bitmap null (a passthrough format, or a source too
    // big to decode on this device); never grows past ~64px on the long side, so it's always cheap
    try {
        return await decodeBitmap(source, bytes, format, { resizeWidth: 64, resizeQuality: 'high' });
    }
    catch (e) {
        errorLog?.log('tryCreatePlaceholderBitmap: decode failed', e);
        return null;
    }
}

/** Decodes via the browser, falling back to the libheif wasm for the HEIC that Chromium and
 *  Firefox cannot read. Trying the browser first keeps WebKit on its native path, and lets a
 *  browser that gains HEIC support drop the fallback on its own. */
async function decodeBitmap(
    source: Blob,
    bytes: Uint8Array,
    format: ImageFormat,
    options?: ImageBitmapOptions,
): Promise<ImageBitmap> {
    const toBitmap = (image: ImageBitmapSource): Promise<ImageBitmap> =>
        options ? createImageBitmap(image, options) : createImageBitmap(image);
    try {
        return await toBitmap(source);
    }
    catch (e) {
        if (format !== 'heif')
            throw e;

        debugLog?.log('decodeBitmap: no native HEIC decode, falling back to libheif');
        const decoder = await getDecoder();
        const image = await decoder?.decode(bytes);
        if (!image)
            throw e;

        return await toBitmap(new ImageData(image.data, image.width, image.height));
    }
}

function createPassthroughOutput(
    source: Blob,
    bytes: Uint8Array,
    format: ImageFormat,
    spec: ImageOutputSpec,
): ImageOutput {
    const data = spec.stripMetadata ? stripImageMetadata(bytes, format) : bytes;
    const mimeType = getImageMimeType(format);
    const isSource = data === bytes;
    const blob = isSource ? source : new Blob([data as BlobPart], { type: mimeType });
    return { kind: spec.kind, blob, mimeType, width: 0, height: 0, isSource };
}

async function reencode(
    bitmap: ImageBitmap,
    source: Blob,
    bytes: Uint8Array,
    format: ImageFormat,
    spec: ImageOutputSpec,
    maxMobileEncodePixels: number,
): Promise<ImageOutput> {
    const target = fitWithinBudget(bitmap.width, bitmap.height, spec.maxPixels, spec.maxLongSide);
    if (!canEncodeOnThisDevice(target.width * target.height, maxMobileEncodePixels))
        return { ...createPassthroughOutput(source, bytes, format, spec), declined: true };

    const canvas = new OffscreenCanvas(target.width, target.height);
    const context = canvas.getContext('2d')!;
    context.imageSmoothingQuality = 'high';
    context.drawImage(bitmap, 0, 0, target.width, target.height);
    const image = context.getImageData(0, 0, target.width, target.height);
    const isUnscaled = target.width === bitmap.width && target.height === bitmap.height;
    if (canHaveAlpha(format) && hasTransparentPixels(image.data)) {
        const png = await canvas.convertToBlob({ type: 'image/png' });
        const kept = isUnscaled && format === 'png'
            ? tryKeepSource(source, bytes, format, spec, target, png.size)
            : null;
        return kept ?? {
            kind: spec.kind,
            blob: png,
            mimeType: 'image/png',
            width: target.width,
            height: target.height,
            isSource: false,
        };
    }

    const jpeg = await encodeJpeg(canvas, image);
    const kept = isUnscaled && format === 'jpeg'
        ? tryKeepSource(source, bytes, format, spec, target, jpeg.size)
        : null;
    return kept ?? {
        kind: spec.kind,
        blob: jpeg,
        mimeType: 'image/jpeg',
        width: target.width,
        height: target.height,
        isSource: false,
    };
}

/** jpegli needs ~8.8 bytes of wasm heap per pixel, on top of the bitmap and the canvas copy;
 *  a phone that runs out does not throw, the OS kills the app. */
export const canEncodeOnThisDevice = (pixels: number, maxMobilePixels: number): boolean => {
    if (!DeviceInfo.isMobile)
        return true;

    if (!(maxMobilePixels > 0)) {
        // Declining is the safe answer either way: a passthrough is bigger, an OOM kill loses the draft
        errorLog?.log(`canEncodeOnThisDevice: no mobile pixel cap in the request: ${maxMobilePixels}`);
        return false;
    }

    return pixels <= maxMobilePixels;
};

async function encodeJpeg(canvas: OffscreenCanvas, image: ImageData): Promise<Blob> {
    const jpeg = await tryEncodeWithRebuild(getEncoder, encoder =>
        encoder.encode(image.data, image.width, image.height, {
            distance: JPEG_DISTANCE,
            subsampling: 420,
            progressive: 2,
        }));
    if (jpeg)
        return new Blob([jpeg as BlobPart], { type: 'image/jpeg' });

    return await canvas.convertToBlob({ type: 'image/jpeg', quality: FALLBACK_JPEG_QUALITY });
}

export async function tryEncodeWithRebuild<T>(
    load: () => Promise<JpegliEncoder | null>,
    encode: (encoder: JpegliEncoder) => T,
): Promise<T | null> {
    const encoder = await load();
    if (!encoder)
        return null;

    try {
        return encode(encoder);
    }
    catch (e) {
        // A wasm trap poisons the instance: every later encode on it traps too, so it must go.
        // A plain Error is jpegli refusing the image, and leaves the instance usable.
        if (e instanceof WebAssembly.RuntimeError)
            whenEncoderLoaded = null;
        errorLog?.log('encode failed', e);
        return null;
    }
}

export function getEncoder(): Promise<JpegliEncoder | null> {
    whenEncoderLoaded ??= JpegliEncoder.load(`${distBaseUrl}/jpegli`).catch((e: unknown) => {
        errorLog?.log('getEncoder: jpegli failed to load, falling back to canvas:', e);
        whenEncoderLoaded = null;
        return null;
    });
    return whenEncoderLoaded;
}

export function getDecoder(): Promise<HeifDecoder | null> {
    whenDecoderLoaded ??= HeifDecoder.load(`${distBaseUrl}/libheif`).catch((e: unknown) => {
        errorLog?.log('getDecoder: libheif failed to load:', e);
        whenDecoderLoaded = null;
        return null;
    });
    return whenDecoderLoaded;
}

function hasTransparentPixels(rgba: Uint8ClampedArray): boolean {
    for (let i = 3; i < rgba.length; i += 4) {
        if (rgba[i] !== 255)
            return true;
    }
    return false;
}
