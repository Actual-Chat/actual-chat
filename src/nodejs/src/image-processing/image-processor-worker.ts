import { rpcServer } from 'rpc';
import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { chooseEncoding } from './image-encoding-policy';
import { getImageMimeType, isAnimatedImage, readImageDimensions, sniffImageFormat } from './image-format';
import { fitWithin } from './image-geometry';
import { JpegliEncoder } from './jpegli-encoder';
import { stripImageMetadata } from './metadata-stripper';
import type {
    ImageFormat,
    ImageOutput,
    ImageOutputSpec,
    ImageProcessorWorker,
    ImageProcessRequest,
    ImageProcessResult,
} from './image-processing-contracts';

const { debugLog, warnLog, errorLog } = getLogs('ImageProcessorWorker');

const JPEG_DISTANCE = 1.9;
// WebKit's canvas encoder maps quality much higher than Chromium's; both land near SSIMULACRA2 74
const FALLBACK_JPEG_QUALITY = DeviceInfo.isWebKit ? 0.5 : 0.75;

let jpegliBaseUrl = '';
let whenEncoderLoaded: Promise<JpegliEncoder | null> | null = null;
let queueTail: Promise<unknown> = Promise.resolve();

const serverImpl: ImageProcessorWorker = {
    init: (baseUrl: string): Promise<void> => {
        jpegliBaseUrl = baseUrl;
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
    const longSide = dimensions ? Math.max(dimensions.width, dimensions.height) : 0;
    const outputs: ImageOutput[] = [];
    let bitmap: ImageBitmap | null = null;
    try {
        for (const spec of request.outputs) {
            const isOversized = spec.maxPassthroughSize !== null && longSide > spec.maxPassthroughSize;
            if (chooseEncoding(format, isAnimated, spec.codec, isOversized) === 'passthrough') {
                outputs.push(createPassthroughOutput(source, bytes, format, spec));
                continue;
            }

            bitmap ??= await createImageBitmap(source);
            outputs.push(await reencode(bitmap, source, bytes, format, spec));
        }
        const elapsedMs = Math.round(performance.now() - startedAt);
        debugLog?.log(`processImage: ${format}, ${outputs.length} output(s) in ${elapsedMs}ms`);
        return { format, outputs };
    }
    finally {
        bitmap?.close();
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
): Promise<ImageOutput> {
    const size = fitWithin(bitmap.width, bitmap.height, spec.maxSize);
    const canvas = new OffscreenCanvas(size.width, size.height);
    const context = canvas.getContext('2d')!;
    context.imageSmoothingQuality = 'high';
    context.drawImage(bitmap, 0, 0, size.width, size.height);
    const image = context.getImageData(0, 0, size.width, size.height);
    if (hasTransparentPixels(image.data)) {
        const png = await canvas.convertToBlob({ type: 'image/png' });
        return {
            kind: spec.kind,
            blob: png,
            mimeType: 'image/png',
            width: size.width,
            height: size.height,
            isSource: false,
        };
    }

    const jpeg = await encodeJpeg(canvas, image);
    const isUnscaledJpeg = format === 'jpeg' && size.width === bitmap.width && size.height === bitmap.height;
    if (isUnscaledJpeg) {
        const stripped = stripImageMetadata(bytes, format);
        if (stripped.length <= jpeg.size) {
            const isSource = stripped === bytes;
            const blob = isSource ? source : new Blob([stripped as BlobPart], { type: 'image/jpeg' });
            return { kind: spec.kind, blob, mimeType: 'image/jpeg', width: size.width, height: size.height, isSource };
        }
    }
    return {
        kind: spec.kind,
        blob: jpeg,
        mimeType: 'image/jpeg',
        width: size.width,
        height: size.height,
        isSource: false,
    };
}

async function encodeJpeg(canvas: OffscreenCanvas, image: ImageData): Promise<Blob> {
    const encoder = await getEncoder();
    if (encoder) {
        try {
            const jpeg = encoder.encode(image.data, image.width, image.height, {
                distance: JPEG_DISTANCE,
                subsampling: 420,
                progressive: 2,
            });
            return new Blob([jpeg as BlobPart], { type: 'image/jpeg' });
        }
        catch (e) {
            warnLog?.log('encodeJpeg: jpegli failed, falling back to canvas:', e);
        }
    }
    return await canvas.convertToBlob({ type: 'image/jpeg', quality: FALLBACK_JPEG_QUALITY });
}

function getEncoder(): Promise<JpegliEncoder | null> {
    whenEncoderLoaded ??= JpegliEncoder.load(jpegliBaseUrl).catch((e: unknown) => {
        errorLog?.log('getEncoder: jpegli failed to load, falling back to canvas:', e);
        whenEncoderLoaded = null;
        return null;
    });
    return whenEncoderLoaded;
}

function hasTransparentPixels(rgba: Uint8ClampedArray): boolean {
    for (let i = 3; i < rgba.length; i += 4) {
        if (rgba[i] !== 255)
            return true;
    }
    return false;
}
