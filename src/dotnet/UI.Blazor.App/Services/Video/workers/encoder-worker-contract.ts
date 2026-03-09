/**
 * Encoder Worker Contract (Universal - Chrome & Safari)
 * Defines the API for the encoder worker using RPC pattern
 */

import { RpcNoWait, RpcTimeout } from 'rpc';
import type { EncoderConfig, EncoderStats } from '../webcodecs-encoder';

/**
 * Serialized chunk data sent from encoder worker to main thread.
 * All ArrayBuffer fields are transferred (zero-copy) via postMessage.
 */
export interface SerializedChunkData {
    chunkBytes: ArrayBuffer;
    timestamp: number;
    duration: number;
    isKeyFrame: boolean;
    codec: string;
    sequenceNumber: number;
    descriptionBytes?: ArrayBuffer;
}

/**
 * Encoder Worker API
 * Represents the SENDER side of the video pipeline
 * This interface is implemented by the worker and called from the main thread
 */
export interface EncoderWorker {
    /**
     * Initialize the encoder with configuration
     * @param config Encoder configuration (codec, bitrate, resolution, etc.)
     * @param timeout Optional RPC timeout configuration
     */
    initialize(config: EncoderConfig, timeout?: RpcTimeout): Promise<void>;

    /**
     * Stop the encoder and clean up resources
     */
    stop(): Promise<void>;

    /**
     * Encode a single video frame
     * @param frame VideoFrame to encode (will be transferred)
     * @param noWait Fire-and-forget flag (don't wait for response)
     */
    encodeFrame(frame: VideoFrame, noWait?: RpcNoWait): Promise<void>;

    /**
     * Flush pending frames in the encoder
     */
    flush(): Promise<void>;

    /**
     * Dynamically reconfigure the encoder
     * @param params New encoding parameters (bitrate, width, height)
     */
    reconfigure(params: { bitrate: number; width: number; height: number }): Promise<void>;

    /**
     * Switch codec: flush and close current encoder, create and configure new encoder
     * @param config New encoder configuration with updated codec string
     */
    switchCodec(config: EncoderConfig): Promise<void>;

    /**
     * Get current encoder statistics
     */
    getStats(): Promise<EncoderStats>;

    /**
     * Force the next encoded frame to be a keyframe.
     * Used when restoring full framerate after VAD silence period.
     */
    forceKeyFrame(): Promise<void>;
}

/**
 * Callbacks from Encoder Worker to Main Thread
 * These are for asynchronous events only (not method responses)
 */
export interface EncoderWorkerCallbacks {
    /**
     * Called when a frame has been encoded and serialized in the worker.
     * All ArrayBuffer args are transferred (zero-copy) via postMessage.
     * @param chunkBytes Raw encoded chunk bytes
     * @param timestamp Chunk timestamp in microseconds
     * @param duration Chunk duration in microseconds
     * @param isKeyFrame Whether this is a keyframe
     * @param codec Actual codec string from encoder output
     * @param sequenceNumber Chunk sequence number for ordering
     * @param descriptionBytes Optional codec description bytes (SPS/PPS for H.264)
     * @param noWait Fire-and-forget flag (don't wait for response)
     */
    onSerializedChunk(
        chunkBytes: ArrayBuffer,
        timestamp: number,
        duration: number,
        isKeyFrame: boolean,
        codec: string,
        sequenceNumber: number,
        descriptionBytes?: ArrayBuffer,
        noWait?: RpcNoWait
    ): Promise<void>;

    /**
     * Called when encoder experiences sustained backpressure (>20% frame drops over 5s).
     * Main thread should step down encoding quality.
     * @param dropRate Fraction of frames dropped (0-1)
     * @param noWait Fire-and-forget
     */
    onBackpressure(dropRate: number, noWait?: RpcNoWait): Promise<void>;
}
