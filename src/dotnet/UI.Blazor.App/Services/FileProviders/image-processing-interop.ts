import { ImageProcessor } from 'image-processing/image-processor';
import type { ImageProcessRequest, ImageProcessResult } from 'image-processing/image-processing-contracts';
import { captureVideoFrame } from 'image-processing/video-frame';
import { getLogs } from 'logging';

const { warnLog } = getLogs('ImageProcessingInterop');

// Twice the placeholder's long side, so its own downscale still averages several pixels into one
const VIDEO_FRAME_LONG_SIDE = 128;

export interface ProcessedImageInfo {
    mimeType: string;
    width: number;
    height: number;
    size: number;
    isSource: boolean;
    declined: boolean;
    placeholder: string;
}

export interface ProcessedStreamImage extends ProcessedImageInfo {
    stream: unknown;
}

export class ImageProcessingInterop {
    /** Processes a local content URL (MAUI) and hands the main output to .NET as a stream. */
    public static async processUrl(url: string, request: ImageProcessRequest): Promise<ProcessedStreamImage> {
        const result = await ImageProcessor.process(url, request);
        const main = getMainOutput(result);
        const stream = main.isSource ? null : DotNet.createJSStreamReference(main.blob);
        return { ...getProcessedImageInfo(result), stream };
    }

    /** Packs a preview - a video's early frame or a ready image thumbnail - as an attachment placeholder;
     *  '' when it can't be read. */
    public static async createPreviewPlaceholder(
        url: string,
        isVideo: boolean,
        request: ImageProcessRequest,
    ): Promise<string> {
        try {
            const source = isVideo ? await captureVideoFrame(url, VIDEO_FRAME_LONG_SIDE) : url;
            const result = await ImageProcessor.process(source, request);
            return result.outputs.find(o => o.kind === 'placeholder')?.placeholder ?? '';
        }
        catch (e) {
            warnLog?.log('createPreviewPlaceholder: failed for', url, e);
            return '';
        }
    }
}

export function getMainOutput(result: ImageProcessResult): ImageProcessResult['outputs'][number] {
    const main = result.outputs.find(o => o.kind === 'main');
    if (!main)
        throw new Error('ImageProcessingInterop: the request has no main output.');

    return main;
}

export function getProcessedImageInfo(result: ImageProcessResult): ProcessedImageInfo {
    const main = getMainOutput(result);
    return {
        mimeType: main.mimeType,
        width: main.width,
        height: main.height,
        size: main.blob.size,
        isSource: main.isSource,
        declined: main.declined ?? false,
        placeholder: result.outputs.find(o => o.kind === 'placeholder')?.placeholder ?? '',
    };
}
