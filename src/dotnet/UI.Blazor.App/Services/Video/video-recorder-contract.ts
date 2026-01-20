/**
 * Video Recorder Contract
 * Defines interfaces for the VideoRecorder service that manages camera capture
 * and coordinates encoding/decoding through web workers.
 */

import type { EncoderConfig } from './webcodecs-encoder';
import type { DecoderConfig } from './webcodecs-decoder';

/**
 * Configuration for the video recorder
 */
export interface VideoRecorderConfig {
    /** Video width in pixels */
    width: number;
    /** Video height in pixels */
    height: number;
    /** Target frame rate */
    frameRate: number;
    /** Codec string (e.g., 'avc1.640028' for H.264 High) */
    codec: string;
    /** Target bitrate in bits per second */
    bitrate: number;
    /** Prefer hardware acceleration */
    preferHardware: boolean;
    /** Device ID for specific camera selection (optional) */
    deviceId?: string;
}

/**
 * Current state of the video recorder
 */
export interface VideoRecorderState {
    /** Whether recording is currently active */
    isRecording: boolean;
    /** Whether camera access has been granted */
    hasCamera: boolean;
    /** Whether encoder is initialized and ready */
    encoderReady: boolean;
    /** Whether decoder is initialized and ready */
    decoderReady: boolean;
    /** Current error message, if any */
    error: string | null;
    /** Number of frames encoded */
    framesEncoded: number;
    /** Number of frames decoded */
    framesDecoded: number;
}

/**
 * Default configuration values
 */
export const DEFAULT_VIDEO_RECORDER_CONFIG: VideoRecorderConfig = {
    width: 1280,
    height: 720,
    frameRate: 30,
    codec: 'avc1.640028', // H.264 High 4.0
    bitrate: 2_000_000, // 2 Mbps
    preferHardware: true,
};

/**
 * Callbacks from VideoRecorder to the UI
 */
export interface VideoRecorderCallbacks {
    /**
     * Called when a decoded frame is ready for rendering
     * @param frame The decoded VideoFrame to render
     */
    onFrame(frame: VideoFrame): void;

    /**
     * Called when the recorder state changes
     * @param state The new state
     */
    onStateChange(state: VideoRecorderState): void;

    /**
     * Called when an error occurs
     * @param error The error that occurred
     */
    onError(error: Error): void;
}

/**
 * Video Recorder interface
 * Manages camera capture and coordinates encoding/decoding through web workers
 */
export interface IVideoRecorder {
    /**
     * Initialize the recorder with the given configuration
     * Sets up camera access and creates encoder/decoder workers
     * @param config Configuration options
     */
    initialize(config?: Partial<VideoRecorderConfig>): Promise<void>;

    /**
     * Start recording and processing video frames
     * Begins capturing from camera and sending frames through encode/decode pipeline
     */
    start(): Promise<void>;

    /**
     * Stop recording
     * Stops camera capture and flushes pending frames
     */
    stop(): Promise<void>;

    /**
     * Get the current state of the recorder
     */
    getState(): VideoRecorderState;

    /**
     * Dispose of all resources
     * Stops recording, closes workers, and releases camera
     */
    dispose(): void;
}

/**
 * Creates encoder configuration from video recorder config
 */
export function createEncoderConfig(config: VideoRecorderConfig): EncoderConfig {
    return {
        codec: config.codec,
        width: config.width,
        height: config.height,
        bitrate: config.bitrate,
        framerate: config.frameRate,
        keyframeInterval: config.frameRate, // Keyframe every second
        latencyMode: 'realtime',
        hardwareAcceleration: config.preferHardware ? 'prefer-hardware' : 'prefer-software',
    };
}

/**
 * Creates decoder configuration from video recorder config
 */
export function createDecoderConfig(config: VideoRecorderConfig): DecoderConfig {
    return {
        codec: config.codec,
        optimizeForLatency: true,
        hardwareAcceleration: config.preferHardware ? 'prefer-hardware' : 'prefer-software',
    };
}
