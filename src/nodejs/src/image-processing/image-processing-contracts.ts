import type { RpcTimeout } from 'rpc';

export type ImageFormat = 'jpeg' | 'png' | 'webp' | 'gif' | 'bmp' | 'heif' | 'avif' | 'svg' | 'unknown';
export type ImageOutputKind = 'main' | 'placeholder';
export type ImageOutputCodec = 'auto' | 'passthrough' | 'placeholder';

export interface ImageOutputSpec {
    kind: ImageOutputKind;
    maxPixels: number | null;
    maxLongSide: number | null;
    codec: ImageOutputCodec;
    stripMetadata: boolean;
    /** A passthrough output above this pixel count is re-encoded within the budget instead. */
    maxPassthroughPixels: number | null;
}

export interface ImageProcessRequest {
    outputs: ImageOutputSpec[];
    /** Declared by Constants.Attachments.MaxMobileEncodePixelCount; no default lives here. */
    maxMobileEncodePixels: number;
}

export interface ImageOutput {
    kind: ImageOutputKind;
    blob: Blob;
    mimeType: string;
    /** 0 for a passthrough output: the image isn't decoded then. */
    width: number;
    height: number;
    /** True when blob is the source itself, i.e. nothing had to change. */
    isSource: boolean;
    /** Set only on the placeholder output: the packed container bytes, base64'd. */
    placeholder?: string;
    /** True when a mobile pixel-budget guard declined the encode instead of running it. */
    declined?: boolean;
}

export interface ImageProcessResult {
    format: ImageFormat;
    outputs: ImageOutput[];
}

export interface ImageProcessorWorker {
    /** The folder holding the wasm subfolders - `jpegli` and `libheif`. */
    init(distBaseUrl: string): Promise<void>;
    /** `timeout` is consumed by the RPC client, the worker never receives it. */
    process(source: Blob, request: ImageProcessRequest, timeout?: RpcTimeout): Promise<ImageProcessResult>;
}
