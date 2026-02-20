/**
 * Encoder Worker Contract (Universal - Chrome & Safari)
 * Defines the API for the encoder worker using RPC pattern
 */

import { RpcNoWait, RpcTimeout } from 'rpc';
import type { EncoderConfig, EncoderStats, EncodedChunkData } from '../webcodecs-encoder';

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
     */
    encodeFrame(frame: VideoFrame): Promise<void>;

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
     * Called when a frame has been encoded (asynchronous event)
     * @param chunkData Encoded chunk data with metadata
     * @param noWait Fire-and-forget flag (don't wait for response)
     */
    onEncodedChunk(chunkData: EncodedChunkData, noWait?: RpcNoWait): Promise<void>;
}
