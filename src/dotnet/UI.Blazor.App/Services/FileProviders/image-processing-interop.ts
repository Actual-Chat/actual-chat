import { ImageProcessor } from 'image-processing/image-processor';
import type { ImageProcessRequest, ImageProcessResult } from 'image-processing/image-processing-contracts';

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
