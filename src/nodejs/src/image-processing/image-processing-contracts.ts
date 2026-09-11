import type { RpcTimeout } from 'rpc';

export type ImageFormat = 'jpeg' | 'png' | 'webp' | 'gif' | 'bmp' | 'heif' | 'avif' | 'svg' | 'unknown';
export type ImageOutputKind = 'main' | 'estimate';
export type ImageOutputCodec = 'auto' | 'passthrough';

export interface ImageOutputSpec {
    kind: ImageOutputKind;
    maxSize: number | null;
    codec: ImageOutputCodec;
    stripMetadata: boolean;
    /** A passthrough output of an image whose long side exceeds this is re-encoded within maxSize instead. */
    maxPassthroughSize: number | null;
}

export interface ImageProcessRequest {
    outputs: ImageOutputSpec[];
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
}

export interface ImageProcessResult {
    format: ImageFormat;
    outputs: ImageOutput[];
}

export interface ImageProcessorWorker {
    init(jpegliBaseUrl: string): Promise<void>;
    /** `timeout` is consumed by the RPC client, the worker never receives it. */
    process(source: Blob, request: ImageProcessRequest, timeout?: RpcTimeout): Promise<ImageProcessResult>;
}
