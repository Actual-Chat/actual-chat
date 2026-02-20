/**
 * Decoder Worker (Universal - Chrome & Safari)
 * Handles video decoding in a dedicated worker thread using RPC communication.
 * Receives encoded chunks and outputs decoded frames via RPC callbacks.
 */

import { rpcClientServer, rpcNoWait } from 'rpc';
import type { DecoderWorker, DecoderWorkerCallbacks } from './decoder-worker-contract';
import { type DecoderConfig, type DecoderStats, WebCodecsDecoder } from '../webcodecs-decoder';
import type { EncodedChunkData } from '../webcodecs-encoder';
import { extractHVCC } from '../hevc-parser';
import { Log } from 'logging';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoDecoder');

// Worker state
let decoder: WebCodecsDecoder | null = null;
let processing = false;
let decoderConfigured = false;
let pendingChunks: EncodedChunkData[] = [];
let currentDecoderConfig: DecoderConfig | null = null;
// eslint-disable-next-line @typescript-eslint/no-unused-vars
let frameCount = 0;

// Chunk ordering state to prevent out-of-order decoding issues
let nextExpectedSequence = 0;
const reorderBuffer = new Map<number, EncodedChunkData>();
let lastKeyframeSequence = -1;
const MAX_REORDER_GAP = 5; // If we receive packets 5+ ahead, assume intermediate ones are lost
let waitingForKeyframe = false; // Flag to indicate we're in error recovery mode

// Process buffered chunks in sequence order
function processBufferedChunks(): void {
    while (reorderBuffer.has(nextExpectedSequence)) {
        const chunk = reorderBuffer.get(nextExpectedSequence)!;
        reorderBuffer.delete(nextExpectedSequence);
        decodeChunk(chunk);
        nextExpectedSequence++;
    }
}

// Extract codec family prefix for comparison (e.g., 'avc1' from 'avc1.640028', 'av01' from 'av01.0.08M.08')
function codecFamily(codec: string): string {
    return codec.substring(0, 4);
}

// Handle codec change: flush+close old decoder, create new one with updated config
function handleCodecChange(chunkData: EncodedChunkData): void {
    const newCodec = chunkData.codec!;
    const oldCodec = currentDecoderConfig!.codec;
    infoLog?.log(`Codec change detected: ${oldCodec} -> ${newCodec}, reconfiguring decoder`);

    // 1. Flush + close old decoder
    if (decoder) {
        try {
            if (decoder.getState() === 'configured') {
                // Can't await in sync context, just close
                decoder.close();
            }
        } catch (error) {
            warnLog?.log('Error closing old decoder during codec switch:', error);
        }
    }

    // 2. Update config with new codec
    currentDecoderConfig = { ...currentDecoderConfig!, codec: newCodec, description: undefined };

    // 3. Create new decoder
    decoder = new WebCodecsDecoder(
        { ...currentDecoderConfig, description: undefined },
        (frame: VideoFrame) => {
            frameCount++;
            void callbacks.onDecodedFrame(frame, rpcNoWait);
        },
        (error) => {
            errorLog?.log('Decoder error:', error);
        }
    );
    decoder.initialize();

    // 4. Reset state
    decoderConfigured = false;
    pendingChunks = [];
    reorderBuffer.clear();
    lastKeyframeSequence = -1;
    waitingForKeyframe = false;
    nextExpectedSequence = chunkData.sequenceNumber;

    infoLog?.log(`Decoder reconfigured for codec ${newCodec}, resuming at sequence #${chunkData.sequenceNumber}`);
}

// Decode a single chunk (guaranteed to be in sequence order)
function decodeChunk(chunkData: EncodedChunkData): void {
    const seq = chunkData.sequenceNumber;

    try {
    // Auto-detect codec change from keyframe data
        if (chunkData.type === 'key' && chunkData.codec && currentDecoderConfig) {
            const incomingFamily = codecFamily(chunkData.codec);
            const currentFamily = codecFamily(currentDecoderConfig.codec);
            if (incomingFamily !== currentFamily) {
                handleCodecChange(chunkData);
                // Fall through to normal keyframe processing below
            }
        }

    // Track keyframes for decoder recovery
        if (chunkData.type === 'key') {
            lastKeyframeSequence = seq;
        }

        // If decoder is closed and this is a keyframe, attempt recovery
        if (decoder && decoder.getState() === 'closed' && chunkData.type === 'key') {
            infoLog?.log(`Decoder closed, attempting recovery with keyframe #${seq}`);

            try {
                // Reinitialize decoder
                decoder = new WebCodecsDecoder(
                    { ...currentDecoderConfig!, description: undefined },
                    (frame: VideoFrame) => {
                        frameCount++;
                        void callbacks.onDecodedFrame(frame, rpcNoWait);
                    },
                    (error) => {
                        errorLog?.log('Decoder error:', error);
                        // Errors during decoding will propagate through decodeChunk() promise rejection
                    }
                );

                decoder.initialize();
                infoLog?.log(`Decoder recovered at keyframe #${seq}`);
                decoderConfigured = false; // Will be set to true when we process this keyframe

                // Update description if available
                if (chunkData.metadata?.decoderConfig?.description) {
                    decoder.updateDescription(chunkData.metadata.decoderConfig.description);
                }
            } catch (error) {
                errorLog?.log('Failed to recover decoder:', error);
                // Error logged, will continue trying to decode
                return;
            }
        }

        // If decoder is still closed (not a keyframe or recovery failed), skip this chunk
        if (decoder && decoder.getState() === 'closed') {
            if (chunkData.type === 'key') {
                infoLog?.log(`Decoder in error state, but received keyframe #${seq}`);
            } else {
                warnLog?.log(`Decoder in error state, dropping delta chunk #${seq}`);
                return;
            }
        }

        // Handle first keyframe with metadata
        if (!decoderConfigured && chunkData.type === 'key') {
            infoLog?.log(`First keyframe #${seq} received`);

            let description: AllowSharedBufferSource | undefined;

            // Try to get description from encoder metadata first
            if (chunkData.metadata?.decoderConfig?.description) {
                infoLog?.log('Using description from encoder metadata');
                description = chunkData.metadata.decoderConfig.description;
            }
            // For HEVC, try manual HVCC extraction as fallback
            else if (currentDecoderConfig?.codec.startsWith('hev1') || currentDecoderConfig?.codec.startsWith('hvc1')) {
                infoLog?.log('Attempting manual HVCC extraction for HEVC');
                const hvcc = extractHVCC(chunkData.chunk);
                if (hvcc) {
                    infoLog?.log('Successfully extracted HVCC from bitstream');
                    description = hvcc;
                } else {
                    warnLog?.log('Failed to extract HVCC, decoder may fail');
                }
            } else {
                infoLog?.log('No metadata description - decoder will auto-configure');
            }

            // Reconfigure decoder with description if available
            if (description && decoder) {
                infoLog?.log('Reconfiguring decoder with description');
                decoder.updateDescription(description);
            }

            // Mark as configured so we start decoding
            decoderConfigured = true;

            // Decode the keyframe
            if (decoder) {
                decoder.decode(chunkData);
                infoLog?.log(`First keyframe #${seq} decoded successfully`);
            }

            // Process any buffered chunks from before configuration
            if (pendingChunks.length > 0) {
                infoLog?.log('Processing', pendingChunks.length, 'buffered chunks');
                for (const bufferedChunk of pendingChunks) {
                    if (decoder) {
                        decoder.decode(bufferedChunk);
                    }
                }
                pendingChunks = [];
            }

            return;
        }

        // Check decoder state before attempting to decode
        if (decoder && decoder.getState() === 'closed') {
            if (chunkData.type === 'key') {
                infoLog?.log(`Decoder in error state, but received keyframe #${seq}`);
            } else {
                warnLog?.log(`Decoder in error state, dropping delta chunk #${seq}`);
                return;
            }
        }

        // Decode chunks directly
        if (decoderConfigured && decoder) {
            decoder.decode(chunkData);
        } else {
            // Buffer until decoder is configured with first keyframe
            debugLog?.log('Buffering chunk until configured');
            pendingChunks.push(chunkData);
        }
    } catch (error) {
        errorLog?.log(`Error decoding chunk #${seq}:`, error);

        // If we have a recent keyframe, try to recover
        if (lastKeyframeSequence >= 0 && reorderBuffer.has(lastKeyframeSequence)) {
            infoLog?.log(`Attempting recovery from buffered keyframe #${lastKeyframeSequence}`);
            // Recovery will happen naturally when we process the buffered keyframe
        }

    // Error logged, decoding will continue with next chunks
    }
}

// RPC Server Implementation
const serverImpl: DecoderWorker = {
    /**
   * Initialize the decoder
   */
    // eslint-disable-next-line
    initialize: async (config): Promise<void> => {
        try {
            infoLog?.log('Initializing decoder for codec:', config.codec);

            // Store decoder config for later use
            currentDecoderConfig = config;

            // Setup decoder - auto-configure from bitstream
            decoder = new WebCodecsDecoder(
                {
                    ...config,
                    description: undefined // Don't require description - decoder will auto-configure
                },
                (frame: VideoFrame) => {
                    frameCount++;
                    void callbacks.onDecodedFrame(frame, rpcNoWait);
                },
                (error) => {
                    errorLog?.log('Decoder error:', error);
                    // Errors during decoding will be logged, decoder continues
                }
            );

            // Initialize the decoder
            decoder.initialize();
            infoLog?.log('Decoder initialized for codec:', config.codec);

            // Mark as ready
            processing = true;

            infoLog?.log('Ready to decode chunks');
            // No callback needed - initialize() returning successfully means ready
        } catch (error) {
            errorLog?.log('Failed to initialize decoder:', error);
            throw error; // RPC automatically propagates errors
        }
    },

    /**
   * Stop the decoder
   */
    stop: async (): Promise<void> => {
        try {
            infoLog?.log('Stopping decoder...');

            processing = false;
            decoderConfigured = false;

            // Wait for in-flight chunks
            await new Promise(resolve => setTimeout(resolve, 200));

            // Flush and close decoder
            if (decoder) {
                try {
                    await decoder.flush();
                    decoder.close();
                    infoLog?.log('Decoder closed');
                } catch (error) {
                    warnLog?.log('Decoder close error:', error);
                }
            }

            infoLog?.log('Decoder stopped');

            // Reset state
            decoder = null;
            pendingChunks = [];
            currentDecoderConfig = null;
            frameCount = 0;
            nextExpectedSequence = 0;
            reorderBuffer.clear();
            lastKeyframeSequence = -1;
            waitingForKeyframe = false;
        } catch (error) {
            errorLog?.log('Failed to stop decoder:', error);
            throw error; // RPC automatically propagates errors
        }
    },

    /**
   * Decode an encoded chunk
   */
    // eslint-disable-next-line
    decodeChunk: async (chunkData): Promise<void> => {
        if (!processing) {
            warnLog?.log('Dropping chunk - not processing');
            return;
        }

        const seq = chunkData.sequenceNumber;
        // console.log(`[Decoder Worker] Received ${chunkData.type} chunk #${seq} via RPC, size: ${chunkData.byteLength}`);

        // If we're waiting for a keyframe due to packet loss, drop all non-keyframe chunks
        if (waitingForKeyframe && chunkData.type !== 'key') {
            // console.log(`[Decoder Worker] Dropping ${chunkData.type} chunk #${seq} while waiting for keyframe`);
            return;
        }

        // If this is a keyframe and we were waiting for one, reset recovery mode
        if (waitingForKeyframe && chunkData.type === 'key') {
            infoLog?.log(`Recovery keyframe #${seq} received`);
            waitingForKeyframe = false;
            reorderBuffer.clear();
            nextExpectedSequence = seq;
            // Process this keyframe immediately
            decodeChunk(chunkData);
            nextExpectedSequence = seq + 1;
            return;
        }

        // Handle out-of-order delivery: buffer chunks until we can process in sequence
        if (seq !== -1 && seq !== nextExpectedSequence) {
            const gap = seq - nextExpectedSequence;
            debugLog?.log(`Out-of-order chunk #${seq} (expecting #${nextExpectedSequence}), gap:`, gap);
            reorderBuffer.set(seq, chunkData);

            // If gap is too large, likely we have packet loss
            if (gap >= MAX_REORDER_GAP) {
                warnLog?.log(`Gap of ${gap} detected, packet #${nextExpectedSequence} is likely lost`);

                // Check if we have a keyframe in the buffer we can recover from
                let hasKeyframeInBuffer = false;
                let firstKeyframeSeq = -1;
                for (const [bufSeq, bufChunk] of reorderBuffer) {
                    if (bufChunk.type === 'key' && bufSeq > nextExpectedSequence) {
                        hasKeyframeInBuffer = true;
                        firstKeyframeSeq = firstKeyframeSeq === -1 ? bufSeq : Math.min(firstKeyframeSeq, bufSeq);
                    }
                }

                if (hasKeyframeInBuffer) {
                    // We have a keyframe - skip to it and discard all delta frames before it
                    infoLog?.log(`Found keyframe #${firstKeyframeSeq} in buffer, skipping to it`);

                    // Remove all chunks before the keyframe
                    for (const [bufSeq] of reorderBuffer) {
                        if (bufSeq < firstKeyframeSeq) {
                            reorderBuffer.delete(bufSeq);
                        }
                    }

                    // Jump to the keyframe sequence
                    nextExpectedSequence = firstKeyframeSeq;
                    processBufferedChunks();
                } else {
                    // No keyframe available - enter recovery mode
                    warnLog?.log(`No keyframe in buffer after lost packet #${nextExpectedSequence}, entering recovery mode`);
                    waitingForKeyframe = true;
                    reorderBuffer.clear();
                }
                return;
            }

            // If we received a keyframe while waiting, we can reset and skip missing packets
            if (chunkData.type === 'key') {
                debugLog?.log(`Received keyframe #${seq} while waiting for #${nextExpectedSequence}`);
                nextExpectedSequence = seq;
                decodeChunk(chunkData);
                nextExpectedSequence = seq + 1;
                // Clear old buffered chunks before this keyframe
                for (const [bufSeq] of reorderBuffer) {
                    if (bufSeq < seq) {
                        reorderBuffer.delete(bufSeq);
                    }
                }
                processBufferedChunks();
                return;
            }

            // Try to process buffered chunks in order
            processBufferedChunks();
            return;
        }

        // Process this chunk immediately (it's in order)
        decodeChunk(chunkData);

        // Increment and try to process next buffered chunks
        if (seq !== -1) {
            nextExpectedSequence = seq + 1;
            processBufferedChunks();
        }
    },

    /**
   * Flush pending chunks
   */
    flush: async (): Promise<void> => {
        if (decoder) {
            try {
                await decoder.flush();
                infoLog?.log('Decoder flushed');
            } catch (error) {
                warnLog?.log('Decoder flush error:', error);
            }
        }
    },

    /**
   * Get current decoder statistics
   */
    // eslint-disable-next-line
    getStats: async (): Promise<DecoderStats> => {
        return decoder?.getStats() ?? {
            decodedFrames: 0,
            droppedFrames: 0,
            averageDecodeTime: 0,
            hardwareAcceleration: 'unknown',
            resolution: 'N/A'
        };
    },

    /**
   * Toggle between WASM and built-in decoders
   */
    // eslint-disable-next-line
    toggleDecoderType: async (useWasm: boolean): Promise<void> => {
        try {
            infoLog?.log('Toggling decoder type to', useWasm ? 'WASM' : 'built-in');

            if (!decoder) {
                throw new Error('Decoder not initialized');
            }

            // For the regular decoder worker, we need to check if it supports toggling
            // Since WebCodecsDecoder doesn't have toggle functionality, we'll just log this
            infoLog?.log('WebCodecs decoder - using WebCodecs API');

            // If this is an AV1 decoder, we could potentially switch to WASM implementation
            // But for now, we'll just log the request
        } catch (error) {
            errorLog?.log('Failed to toggle decoder type:', error);
            throw error;
        }
    }
};

// Initialize RPC communication (bidirectional)
const callbacks = rpcClientServer<DecoderWorkerCallbacks>(
    'DecoderWorker',
  self as unknown as Worker,
  serverImpl
);

infoLog?.log('Decoder worker initialized');
