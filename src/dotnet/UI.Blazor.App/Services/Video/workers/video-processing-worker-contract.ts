/**
 * Video Processing Worker Contract
 * Unified worker that combines segmentation, encoding, and SignalR streaming.
 * All video frame processing happens in a single worker context — no cross-worker
 * VideoFrame transfer needed. Encoded chunks go directly to SignalR from the worker.
 *
 * Also contains segmentation config types and model configuration utilities
 * (previously in segmentation-worker-contract.ts).
 */

import { RpcNoWait, RpcTimeout } from 'rpc';
import type { EncoderConfig, EncoderStats } from '../webcodecs-encoder';
import { Log } from 'logging';

const { debugLog, warnLog } = Log.get('VideoSegmentation');

// ─── Segmentation config types ──────────────────────────────────────────────

export type TensorFormat = 'nhwc_uint8' | 'nchw_float32';
export type OutputFormat = 'single_channel' | 'multi_channel_nchw';
export type OutputLayout = 'nhwc' | 'nchw';

export interface ModelConfig {
    tensorFormat: TensorFormat;
    outputFormat?: OutputFormat;
    outputLayout?: OutputLayout;
    outputDataType?: 'float32';
    outputChannels?: number;
    backgroundChannelIndex?: number;
}

export const MODEL_CONFIGS: Record<string, ModelConfig> = {
    'selfie_segmentation_olive_webgpu.onnx': {
        tensorFormat: 'nchw_float32',
        outputFormat: 'single_channel',
        outputLayout: 'nchw',
        outputDataType: 'float32',
        outputChannels: 1,
        backgroundChannelIndex: 0,
    },
};

export const DEFAULT_MODEL_CONFIG: ModelConfig = {
    tensorFormat: 'nchw_float32',
    outputFormat: 'single_channel',
    outputLayout: 'nchw',
    outputDataType: 'float32',
    outputChannels: 1,
    backgroundChannelIndex: 0,
};

export interface SegmentationConfig {
    backend: 'webgpu' | 'webgl' | 'wasm';
    modelUrl?: string;
    blurEnabled: boolean;
    blurRadius: number;
    inputWidth: number;
    inputHeight: number;
    maskThreshold: number;
    frameSkipInterval?: number;
    maxQueueSize: number;
    temporalSmoothingFactor?: number;
    outputWidth?: number;
    outputHeight?: number;
    modelConfig?: ModelConfig;
}

export interface SegmentationStats {
    processedFrames: number;
    averageInferenceTime: number;
    averageBlurTime: number;
    averageTotalTime: number;
    droppedFrames: number;
    backend: string;
}

export const DEFAULT_SEGMENTATION_CONFIG = {
    blurRadius: 12,
    inputWidth: 256,
    inputHeight: 256,
    maskThreshold: 0.45,
    maxQueueSize: 5,
    frameSkipInterval: 1,
    temporalSmoothingFactor: 0.8,
} as const;

function extractFilename(url: string): string {
    const cleanUrl = url.split('?')[0].split('#')[0];
    const filename = cleanUrl.split('/').pop() ?? '';
    return filename.replace(/-[A-Z0-9]{8}\./i, '.');
}

export function getModelConfig(modelUrl: string): ModelConfig {
    const filename = extractFilename(modelUrl);

    if (filename in MODEL_CONFIGS)
        return MODEL_CONFIGS[filename];

    for (const [key, config] of Object.entries(MODEL_CONFIGS)) {
        if (extractFilename(key) === filename)
            return config;
    }

    warnLog?.log('No model config found for:', filename, '- using default');
    return DEFAULT_MODEL_CONFIG;
}

export function createDefaultSegmentationConfig(backend: SegmentationConfig['backend']): SegmentationConfig {
    return { backend, blurEnabled: true, ...DEFAULT_SEGMENTATION_CONFIG };
}

export function createAdaptiveSegmentationConfig(backend: SegmentationConfig['backend']): SegmentationConfig {
    const isMobile = /iPhone|iPad|Android/i.test(navigator.userAgent);
    if (!isMobile)
        return { backend, blurEnabled: true, ...DEFAULT_SEGMENTATION_CONFIG };

    debugLog?.log('Using mobile-optimized segmentation config');
    return {
        backend, blurEnabled: true, ...DEFAULT_SEGMENTATION_CONFIG,
        blurRadius: 10, frameSkipInterval: 3, maxQueueSize: 3, temporalSmoothingFactor: 0.25,
    };
}

// ─── Video processing worker contract ───────────────────────────────────────

/**
 * Configuration for the unified video processing worker.
 */
export interface VideoProcessingConfig {
    /** Encoder settings (codec, bitrate, resolution, etc.) */
    encoder: EncoderConfig;
    /** Segmentation settings — omit for no blur */
    segmentation?: SegmentationConfig;
    /** Adaptive framerate settings */
    adaptiveFramerate?: { reducedFps: number };
    /** SignalR streaming configuration */
    streaming: {
        hubUrl: string;
        sessionToken: string;
        chatId: string;
        serverClockOffsetMs: number;
        streamKind?: number; // 0 = Webcam (default), 1 = Screencast
    };
    /** When true, skip encoder + SignalR. Only run segmentation and send preview frames. */
    previewOnly?: boolean;
}

export interface VideoProcessingStats {
    encoder: EncoderStats;
    segmentation: SegmentationStats | null;
}

export interface VideoProcessingWorker {
    startWithStream(config: VideoProcessingConfig, frameInputStream: ReadableStream<VideoFrame>, timeout?: RpcTimeout): Promise<void>;
    startWithTrack(config: VideoProcessingConfig, track: MediaStreamTrack, timeout?: RpcTimeout): Promise<void>;
    initialize(config: VideoProcessingConfig, timeout?: RpcTimeout): Promise<void>;
    encodeFrame(frame: VideoFrame, noWait?: RpcNoWait): Promise<void>;

    setVadState(speaking: boolean, remoteStreamCount: number): Promise<void>;
    reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void>;
    switchCodec(config: EncoderConfig): Promise<void>;
    toggleBlur(enabled: boolean, segConfig?: SegmentationConfig): Promise<void>;
    forceKeyFrame(): Promise<void>;
    flush(): Promise<void>;
    stop(): Promise<void>;
    getStats(): Promise<VideoProcessingStats>;

    updateSessionToken(token: string): Promise<void>;
    updateServerClockOffset(offsetMs: number): Promise<void>;
}

export interface VideoProcessingWorkerCallbacks {
    onSerializedChunk(chunkBytes: ArrayBuffer, timestamp: number, duration: number,
        isKeyFrame: boolean, codec: string, sequenceNumber: number,
        descriptionBytes?: ArrayBuffer, noWait?: RpcNoWait): Promise<void>;
    onBackpressure(dropRate: number, noWait?: RpcNoWait): Promise<void>;
    onEncoderFailed(codec: string, noWait?: RpcNoWait): Promise<void>;
    onDimensionReconciled(width: number, height: number, noWait?: RpcNoWait): Promise<void>;
    onPreviewFrame(frame: VideoFrame, noWait?: RpcNoWait): Promise<void>;
    onStreamCreated(codecSettings: string, noWait?: RpcNoWait): Promise<void>;
}
