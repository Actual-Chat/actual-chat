/**
 * Decoder Worker Contract (Universal - Chrome & Safari)
 * Defines the API for the decoder worker using RPC pattern
 */

import { RpcNoWait, RpcTimeout } from 'rpc';
import type { DecoderConfig, DecoderStats } from '../webcodecs-decoder';
import type { EncodedChunkData } from '../webcodecs-encoder';

/**
 * Decoder Worker API
 * Represents the RECEIVER side of the video pipeline
 * This interface is implemented by the worker and called from the main thread
 */
export interface DecoderWorker {
    /**
     * Initialize the decoder with configuration
     * @param config Decoder configuration (codec, description, etc.)
     * @param timeout Optional RPC timeout configuration
     */
    initialize(config: DecoderConfig, timeout?: RpcTimeout): Promise<void>;

    /**
     * Stop the decoder and clean up resources
     */
    stop(): Promise<void>;

    /**
     * Decode an encoded chunk (used by pipeline loopback path — kept for backwards compat)
     * @param chunkData Encoded chunk data to decode
     */
    decodeChunk(chunkData: EncodedChunkData): Promise<void>;

    /**
     * Decode raw encoded bytes (used by video-player.ts).
     * The worker creates EncodedVideoChunk internally from the raw bytes.
     * ArrayBuffer args are transferable (zero-copy to worker).
     * @param data Raw encoded bytes
     * @param timestamp Timestamp in microseconds
     * @param duration Duration in microseconds
     * @param isKeyFrame Whether this is a keyframe
     * @param sequenceNumber Chunk sequence number for ordering
     * @param description Optional codec description bytes (transferred, zero-copy)
     */
    decodeRawChunk(
        data: ArrayBuffer,
        timestamp: number,
        duration: number,
        isKeyFrame: boolean,
        sequenceNumber: number,
        description?: ArrayBuffer
    ): Promise<void>;

    /**
     * Reset the decoder (flush internal queue).
     * Used for tab visibility restore handling.
     */
    resetDecoder(): Promise<void>;

    /**
     * Reconfigure the decoder with new config.
     * Used after reset for tab visibility restore.
     * @param config New decoder configuration
     */
    configureDecoder(config: DecoderConfig): Promise<void>;

    /**
     * Flush pending chunks in the decoder
     */
    flush(): Promise<void>;

    /**
     * Get current decoder statistics
     */
    getStats(): Promise<DecoderStats>;

    /**
     * Toggle between WASM and built-in decoders
     * @param useWasm Whether to use WASM (dav1d.js) decoder or built-in decoder
     */
    toggleDecoderType(useWasm: boolean): Promise<void>;
}

/**
 * Callbacks from Decoder Worker to Main Thread
 * These are for asynchronous events only (not method responses)
 */
export interface DecoderWorkerCallbacks {
    /**
     * Called when a frame has been decoded (asynchronous event)
     * @param frame Decoded VideoFrame (will be transferred)
     * @param noWait Fire-and-forget flag (don't wait for response)
     */
    onDecodedFrame(frame: VideoFrame, noWait?: RpcNoWait): Promise<void>;
}
