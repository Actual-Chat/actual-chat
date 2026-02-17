/**
 * Segmentation Worker RPC Contract
 * Type-safe interface for background blur segmentation processing
 */

import { RpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';

/**
 * Tensor format types supported by different ONNX models.
 *
 * - `nhwc_uint8`: Channels-last format with uint8 values [0-255].
 *   Used by older MediaPipe models (e.g., model_fp16.onnx).
 *   Shape: [batch, height, width, channels] = [1, 256, 256, 3]
 *
 * - `nchw_float32`: Channels-first format with normalized float32 values.
 *   Used by newer selfie segmentation models (e.g., selfie_segmentation_webgpu.onnx).
 *   Shape: [batch, channels, height, width] = [1, 3, 256, 256]
 *   Values are typically normalized to [0, 1] or [-1, 1] range.
 *
 * - `nchw_float16`: Channels-first format with float16 precision.
 *   Used by quantized models with float16 precision (e.g., selfie_segmentation_olive_webgpu_fp16.onnx).
 *   Shape: [batch, channels, height, width] = [1, 3, 256, 256]
 *   Values are typically normalized to [0, 1] or [-1, 1] range with reduced precision.
 */
export type TensorFormat = 'nhwc_uint8' | 'nchw_float32';

/**
 * Output tensor format types for segmentation models.
 *
 * - `single_channel`: Single-channel mask output float32[1, H, W] or float32[1, 1, H, W]
 *   Most common format, values typically in [0, 1] range representing person probability.
 *
 * - `multi_channel_nchw`: Multi-channel output float32[1, C, H, W] where C >= 2
 *   Used by models that output separate channels for person/background.
 *   Requires specifying which channel index contains the background mask.
 */
export type OutputFormat = 'single_channel' | 'multi_channel_nchw';

/**
 * Output tensor layout for single-channel mask outputs.
 * Specifies the dimension ordering of the output tensor.
 *
 * - `nhwc`: Channels-last layout [N, H, W, C] → float32[1, 256, 256, 1]
 *   Height and Width are in their natural positions.
 *   Access pattern: data[y * width + x] for pixel (x, y)
 *
 * - `nchw`: Channels-first layout [N, C, H, W] → float32[1, 1, 256, 256]
 *   Channel dimension comes before spatial dimensions.
 *   Access pattern: data[y * width + x] for pixel (x, y) (same for C=1)
 *
 * IMPORTANT: While memory layout is identical for C=1, the dimension order
 * affects how downstream code interprets the tensor shape. Explicitly
 * specifying the layout prevents misinterpretation bugs.
 */
export type OutputLayout = 'nhwc' | 'nchw';

/**
 * Configuration for model tensor input/output format.
 * Different ONNX models expect different tensor layouts and value ranges.
 */
export interface ModelConfig {
  /**
   * Model input tensor format.
   * - 'nhwc_uint8': uint8[1,256,256,3] - channels-last, values 0-255
   * - 'nchw_float32': float32[1,3,256,256] - channels-first, values in [0, 1]
   * - 'nchw_float16': float16[1,3,256,256] - channels-first, values in [0, 1] with reduced precision
   */
  tensorFormat: TensorFormat;

  /**
   * Output tensor format. Defaults to 'single_channel' for backward compatibility.
   * - 'single_channel': float32[1, H, W] or float32[1, 1, H, W]
   * - 'multi_channel_nchw': float32[1, C, H, W] where C >= 2
   */
  outputFormat?: OutputFormat;

  /**
   * Output tensor dimension layout for single-channel outputs.
   * - 'nhwc': [1, H, W, 1] → float32[1, 256, 256, 1]
   * - 'nchw': [1, 1, H, W] → float32[1, 1, 256, 256]
   * Default: 'nhwc' for backward compatibility.
   */
  outputLayout?: OutputLayout;

  /**
   * Output tensor data type.
   * - 'float32': 4 bytes per element (default for most models)
   * - 'float16': 2 bytes per element (for fp16 quantized models)
   * Default: 'float32' for backward compatibility.
   */
  outputDataType?: 'float32';

  /**
   * Number of output channels. Only relevant for 'multi_channel_nchw' format.
   * Default: 1 (single channel mask)
   */
  outputChannels?: number;

  /**
   * Which channel index (0-based) contains the background/person mask to use.
   * For 'multi_channel_nchw' format with outputChannels >= 2:
   *   - 0: first channel is background (use directly as person mask inverted)
   *   - 1: second channel is background
   * Default: 0
   */
  backgroundChannelIndex?: number;
}

/**
 * Predefined model configurations for known ONNX models.
 * Maps model filenames to their expected tensor format configurations.
 * Used by getModelConfig() for filename-based matching.
 */
export const MODEL_CONFIGS: Record<string, ModelConfig> = {
    // Selfie segmentation model (Olive-optimized for WebGPU)
    // Single channel output NCHW [1,1,256,256]
    'selfie_segmentation_olive_webgpu.onnx': {
        tensorFormat: 'nchw_float32',
        outputFormat: 'single_channel',
        outputLayout: 'nchw',
        outputDataType: 'float32',
        outputChannels: 1,
        backgroundChannelIndex: 0
    },
};

/**
 * Default model configuration matching the bundled selfie segmentation model.
 */
export const DEFAULT_MODEL_CONFIG: ModelConfig = {
    tensorFormat: 'nchw_float32',
    outputFormat: 'single_channel',
    outputLayout: 'nchw',
    outputDataType: 'float32',
    outputChannels: 1,
    backgroundChannelIndex: 0
};

export interface SegmentationConfig {
  /** Backend to use for ONNX Runtime inference */
  backend: 'webgpu' | 'webgl' | 'wasm';

  /** URL to the ONNX model file (optional; worker resolves from bundled import) */
  modelUrl?: string;

  /** Whether background blur is enabled */
  blurEnabled: boolean;

  /** Blur radius for background blur effect (pixels) */
  blurRadius: number;

  /** Input dimensions for the model (typically 256x256 for MediaPipe) */
  inputWidth: number;
  inputHeight: number;

  /** Threshold for person mask (0-1, default 0.5) */
  maskThreshold: number;

  /** Number of frames to skip between inferences (0 = no skip) */
  inferenceSkipFrames: number;

  /** Frame skip interval to reduce GPU contention (default: 1 = no skip) */
  frameSkipInterval?: number;

  /** Maximum frames to skip even with no motion (default: 5) */
  maxSkipFrames: number;

  /** Maximum queue size before dropping oldest frames */
  maxQueueSize: number;

  /**
   * Model tensor format configuration.
   * If not specified, will be auto-detected from MODEL_CONFIGS based on modelUrl,
   * or fall back to DEFAULT_MODEL_CONFIG for backward compatibility.
   */
  modelConfig?: ModelConfig;
}

export interface SegmentationStats {
  /** Total frames processed */
  processedFrames: number;

  /** Average inference time in milliseconds */
  averageInferenceTime: number;

  /** Average blur processing time in milliseconds */
  averageBlurTime: number;

  /** Average total processing time per frame in milliseconds */
  averageTotalTime: number;

  /** Number of frames dropped due to processing delays */
  droppedFrames: number;

  /** Current backend being used */
  backend: string;
}

export interface SegmentationWorkerCallbacks {
  // Callbacks for segmentation worker events
  // Currently empty as we use direct RPC returns
}

// Default segmentation configuration values
export const DEFAULT_SEGMENTATION_CONFIG = {
    /** Default blur radius for background blur effect (pixels) */
    blurRadius: 15,

    /** Default input dimensions for the model */
    inputWidth: 256,
    inputHeight: 256,

    /** Default threshold for person mask (0-1) */
    maskThreshold: 0.5,

    /** Default inference skip frames (0 = no skip) */
    inferenceSkipFrames: 0,

    /** Default maximum frames to skip */
    maxSkipFrames: 5,

    /** Default maximum queue size */
    maxQueueSize: 5,

    /** Default frame skip interval to reduce GPU contention */
    frameSkipInterval: 2,
} as const;

/**
 * Extract the filename from a URL or path.
 * Handles various formats: full URLs, absolute paths, relative paths,
 * and esbuild asset URLs (e.g., /dist/assets/onnx/selfie_segmentation_olive_webgpu-HASH.onnx).
 *
 * For esbuild-hashed filenames, strips the hash suffix to match the original name.
 *
 * @param url - The URL or path to extract filename from
 * @returns The filename without path, query string, or esbuild hash
 */
function extractFilename(url: string): string {
    // Remove query string and hash
    const cleanUrl = url.split('?')[0].split('#')[0];
    // Extract filename from path
    const filename = cleanUrl.split('/').pop() ?? '';
    // Strip esbuild content hash (e.g., "model-ABCDEF12.onnx" -> "model.onnx")
    return filename.replace(/-[A-Z0-9]{8}\./i, '.');
}

/**
 * Get the model configuration for a given model URL.
 * Matches by filename (stripping esbuild hashes and path prefixes).
 * Falls back to DEFAULT_MODEL_CONFIG if no match is found.
 *
 * @param modelUrl - The URL/path of the ONNX model
 * @returns The ModelConfig for the specified model
 */
export function getModelConfig(modelUrl: string): ModelConfig {
    const filename = extractFilename(modelUrl);

    // Try exact key match first (keys are now filenames)
    if (MODEL_CONFIGS[filename]) {
        console.log(`[getModelConfig] Match for filename: ${filename}`);
        return MODEL_CONFIGS[filename];
    }

    // Try matching by extracted filename from keys
    for (const [key, config] of Object.entries(MODEL_CONFIGS)) {
        if (extractFilename(key) === filename) {
            console.log(`[getModelConfig] Filename match: "${filename}" (URL: ${modelUrl} -> Key: ${key})`);
            return config;
        }
    }

    // No match found, use default
    console.warn(`[getModelConfig] No config found for URL: ${modelUrl}, filename: ${filename}. Using default config.`);
    return DEFAULT_MODEL_CONFIG;
}

/**
 * Create a default SegmentationConfig with the specified backend
 * @param backend The ONNX Runtime backend to use
 * @returns Complete SegmentationConfig with default values
 */
export function createDefaultSegmentationConfig(backend: SegmentationConfig['backend']): SegmentationConfig {
    return {
        backend,
        blurEnabled: true, // Default to enabled for backward compatibility
        ...DEFAULT_SEGMENTATION_CONFIG
    };
}

/**
 * Create an adaptive SegmentationConfig optimized for the device and backend
 * @param backend The ONNX Runtime backend to use
 * @returns Complete SegmentationConfig with device-adaptive values
 */
export function createAdaptiveSegmentationConfig(backend: SegmentationConfig['backend']): SegmentationConfig {
    // TEMPORARILY: Use same config for mobile and desktop to test pipeline
    // TODO: Re-enable mobile optimizations after pipeline is working
    const isMobile = /iPhone|iPad|Android/i.test(navigator.userAgent);

    if (!isMobile) {
    // Desktop: no skipping, full quality
        return {
            backend,
            blurEnabled: true,
            ...DEFAULT_SEGMENTATION_CONFIG,
            inferenceSkipFrames: 0
        };
    }

    // TEMP: Use desktop-like config for mobile to test pipeline
    console.log('[AdaptiveConfig] Using desktop-like config for mobile testing');
    return {
        backend,
        blurEnabled: true,
        ...DEFAULT_SEGMENTATION_CONFIG,
        inferenceSkipFrames: 0   // No skipping for testing
    };
}

export interface SegmentationWorkerCallbacks {
  // Callbacks for segmentation worker events
  onFrameProcessed: (frame: VideoFrame, sequenceNumber: number, processingTime: number) => void;
  onError: (error: Error) => void;
}

export interface SegmentationWorker extends Disposable {
  /**
   * Initialize the segmentation worker with ONNX Runtime and load the model
   * @param config Configuration for segmentation processing
   * @param timeoutMs Optional timeout for initialization
   */
  initialize(
    config: SegmentationConfig,
    options?: { timeoutMs?: number }
  ): Promise<void>;

  /**
   * Process a single VideoFrame with segmentation and background blur
   * @param frame Input VideoFrame from camera/screen capture
   * @returns Processed VideoFrame with background blur applied
   */
  processFrame(frame: VideoFrame, _noWait: RpcNoWait): Promise<void>;

  /**
   * Update segmentation configuration (e.g., blur intensity)
   * @param config Partial configuration to update
   */
  updateConfig(config: Partial<SegmentationConfig>): Promise<void>;

  /**
   * Get current performance statistics
   */
  getStats(): Promise<SegmentationStats>;

  /**
   * Stop processing and clean up resources
   */
  stop(): Promise<void>;
}

// Message types for worker communication
export interface InitMessage {
  type: 'init';
  config: SegmentationConfig;
  timeoutMs?: number;
}

export interface ProcessFrameMessage {
  type: 'process-frame';
  frame: VideoFrame;
  sequenceNumber: number;
}

export interface UpdateConfigMessage {
  type: 'update-config';
  config: Partial<SegmentationConfig>;
}

export interface GetStatsMessage {
  type: 'get-stats';
}

export interface StopMessage {
  type: 'stop';
}

export type SegmentationWorkerMessage =
  | InitMessage
  | ProcessFrameMessage
  | UpdateConfigMessage
  | GetStatsMessage
  | StopMessage;

// Response message types
export interface ReadyMessage {
  type: 'ready';
}

export interface FrameProcessedMessage {
  type: 'frame-processed';
  frame: VideoFrame;
  sequenceNumber: number;
  processingTime: number;
}

export interface StatsMessage {
  type: 'stats';
  stats: SegmentationStats;
}

export interface ErrorMessage {
  type: 'error';
  error: string;
  code?: string;
}

export interface StoppedMessage {
  type: 'stopped';
}

export type SegmentationWorkerResponse =
  | ReadyMessage
  | FrameProcessedMessage
  | StatsMessage
  | ErrorMessage
  | StoppedMessage;
