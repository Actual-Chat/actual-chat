/**
 * Decoder Worker (Universal - Chrome & Safari)
 * Handles video decoding in a dedicated worker thread using RPC communication.
 * Receives encoded chunks and outputs decoded frames via RPC callbacks.
 */

import { rpcClientServer, rpcNoWait, rpcServer } from 'rpc';
import type { DecoderWorker, DecoderWorkerCallbacks } from './decoder-worker-contract';
import { WebCodecsDecoder, type DecoderConfig, type DecoderStats } from '../webcodecs-decoder';
import type { EncodedChunkData } from '../webcodecs-encoder';
import { extractHVCC } from '../hevc-parser';

// Worker state
let decoder: WebCodecsDecoder | null = null;
let processing = false;
let decoderConfigured = false;
let pendingChunks: EncodedChunkData[] = [];
let currentDecoderConfig: DecoderConfig | null = null;
let frameCount = 0;

// Chunk ordering state to prevent out-of-order decoding issues
let nextExpectedSequence = 0;
let reorderBuffer: Map<number, EncodedChunkData> = new Map();
let lastKeyframeSequence = -1;
const MAX_REORDER_GAP = 5; // If we receive packets 5+ ahead, assume intermediate ones are lost
let waitingForKeyframe = false; // Flag to indicate we're in error recovery mode

// RPC callbacks to main thread (initialized below)
let callbacks: DecoderWorkerCallbacks;

// Process buffered chunks in sequence order
async function processBufferedChunks(): Promise<void> {
  while (reorderBuffer.has(nextExpectedSequence)) {
    const chunk = reorderBuffer.get(nextExpectedSequence)!;
    reorderBuffer.delete(nextExpectedSequence);
    console.log(`[Decoder Worker] Processing buffered chunk #${nextExpectedSequence}`);
    await decodeChunk(chunk);
    nextExpectedSequence++;
  }
}

// Decode a single chunk (guaranteed to be in sequence order)
function decodeChunk(chunkData: EncodedChunkData): void {
  const seq = chunkData.sequenceNumber ?? -1;

  try {
    // Track keyframes for decoder recovery
    if (chunkData.type === 'key') {
      lastKeyframeSequence = seq;
    }

    // If decoder is closed and this is a keyframe, attempt recovery
    if (decoder && decoder.getState() === 'closed' && chunkData.type === 'key') {
      console.log(`[Decoder Worker] Decoder closed, attempting recovery with keyframe #${seq}`);

      try {
        // Reinitialize decoder
        decoder = new WebCodecsDecoder(
          { ...currentDecoderConfig!, description: undefined },
          async (frame: VideoFrame) => {
            frameCount++;
            if (frameCount % 30 === 1) {
              const timestampSeconds = frame.timestamp / 1_000_000; // Convert microseconds to seconds
              console.log(`[Decoder Worker] Decoded frame #${frameCount}: ${frame.displayWidth}x${frame.displayHeight}, timestamp: ${frame.timestamp}μs (${timestampSeconds.toFixed(2)}s)`);
            }
            // Send frame via RPC callback (fire-and-forget for performance)
            await callbacks.onDecodedFrame(frame, rpcNoWait);
          },
          (error) => {
            console.error('[Decoder Worker] Decoder error:', error);
            // Errors during decoding will propagate through decodeChunk() promise rejection
          }
        );

        decoder.initialize();
        console.log(`[Decoder Worker] Decoder recovered and reinitialized at keyframe #${seq}`);
        decoderConfigured = false; // Will be set to true when we process this keyframe

        // Update description if available
        if (chunkData.metadata?.decoderConfig?.description) {
          decoder.updateDescription(chunkData.metadata.decoderConfig.description);
        }
      } catch (error) {
        console.error('[Decoder Worker] Failed to recover decoder:', error);
        // Error logged, will continue trying to decode
        return;
      }
    }

    // If decoder is still closed (not a keyframe or recovery failed), skip this chunk
    if (decoder && decoder.getState() === 'closed') {
      if (chunkData.type === 'key') {
        console.log(`[Decoder Worker] Decoder in error state, but received keyframe #${seq} - recovery attempted above`);
      } else {
        console.warn(`[Decoder Worker] Decoder in error state, dropping delta chunk #${seq}. Waiting for keyframe to recover.`);
        return;
      }
    }

    // Handle first keyframe with metadata
    if (!decoderConfigured && chunkData.type === 'key') {
      console.log(`[Decoder Worker] First keyframe #${seq} received`);

      let description: AllowSharedBufferSource | undefined;

      // Try to get description from encoder metadata first
      if (chunkData.metadata?.decoderConfig?.description) {
        console.log('[Decoder Worker] Using description from encoder metadata');
        description = chunkData.metadata.decoderConfig.description;
      }
      // For HEVC, try manual HVCC extraction as fallback
      else if (currentDecoderConfig?.codec.startsWith('hev1') || currentDecoderConfig?.codec.startsWith('hvc1')) {
        console.log('[Decoder Worker] No metadata description, attempting manual HVCC extraction for HEVC');
        const hvcc = extractHVCC(chunkData.chunk);
        if (hvcc) {
          console.log('[Decoder Worker] Successfully extracted HVCC from bitstream');
          description = hvcc;
        } else {
          console.warn('[Decoder Worker] Failed to extract HVCC, decoder may fail');
        }
      } else {
        console.log('[Decoder Worker] No metadata description - decoder will auto-configure from bitstream');
      }

      // Reconfigure decoder with description if available
      if (description && decoder) {
        console.log('[Decoder Worker] Reconfiguring decoder with description');
        decoder.updateDescription(description);
        console.log('[Decoder Worker] Decoder reconfigured');
      }

      // Mark as configured so we start decoding
      decoderConfigured = true;

      // Decode the keyframe
      if (decoder) {
        decoder.decode(chunkData);
        console.log(`[Decoder Worker] First keyframe #${seq} decoded successfully`);
      }

      // Process any buffered chunks from before configuration
      if (pendingChunks.length > 0) {
        console.log(`[Decoder Worker] Processing ${pendingChunks.length} pre-configuration buffered chunks`);
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
        console.log(`[Decoder Worker] Decoder in error state, but received keyframe #${seq} - recovery attempted above`);
      } else {
        console.warn(`[Decoder Worker] Decoder in error state, dropping delta chunk #${seq}. Waiting for keyframe to recover.`);
        return;
      }
    }

    // Decode chunks directly
    if (decoderConfigured && decoder) {
      decoder.decode(chunkData);
    } else {
      // Buffer until decoder is configured with first keyframe
      console.log('[Decoder Worker] Buffering chunk until decoder is configured');
      pendingChunks.push(chunkData);
    }
  } catch (error) {
    console.error(`[Decoder Worker] Error decoding chunk #${seq}:`, error);

    // If we have a recent keyframe, try to recover
    if (lastKeyframeSequence >= 0 && reorderBuffer.has(lastKeyframeSequence)) {
      console.log(`[Decoder Worker] Attempting recovery from buffered keyframe #${lastKeyframeSequence}`);
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
  initialize: async (config): Promise<void> => {
    try {
      console.log(`[Decoder Worker] Initializing decoder via RPC with codec: ${config.codec}`);

      // Store decoder config for later use
      currentDecoderConfig = config;

      // Setup decoder - auto-configure from bitstream
      decoder = new WebCodecsDecoder(
        {
          ...config,
          description: undefined // Don't require description - decoder will auto-configure
        },
        async (frame: VideoFrame) => {
          frameCount++;
          if (frameCount % 30 === 1) { // Log every 30th frame to avoid console spam
            const timestampSeconds = frame.timestamp / 1_000_000; // Convert microseconds to seconds
            console.log(`[Decoder Worker] Decoded frame #${frameCount}: ${frame.displayWidth}x${frame.displayHeight}, timestamp: ${frame.timestamp}μs (${timestampSeconds.toFixed(2)}s)`);
          }
          // Send decoded frame back to main thread via RPC callback (fire-and-forget)
          await callbacks.onDecodedFrame(frame, rpcNoWait);
        },
        (error) => {
          console.error('[Decoder Worker] Decoder error:', error);
          // Errors during decoding will be logged, decoder continues
        }
      );

      // Initialize the decoder
      await decoder.initialize();
      console.log(`[Decoder Worker] Decoder initialized via RPC for codec: ${config.codec}`);

      // Mark as ready
      processing = true;

      console.log('[Decoder Worker] Ready to decode chunks');
      // No callback needed - initialize() returning successfully means ready
    } catch (error) {
      console.error('[Decoder Worker] Failed to initialize decoder:', error);
      throw error; // RPC automatically propagates errors
    }
  },

  /**
   * Stop the decoder
   */
  stop: async (): Promise<void> => {
    try {
      console.log('[Decoder Worker] Stopping decoder via RPC...');

      processing = false;
      decoderConfigured = false;

      // Wait for in-flight chunks
      await new Promise(resolve => setTimeout(resolve, 200));

      // Flush and close decoder
      if (decoder) {
        try {
          await decoder.flush();
          decoder.close();
          console.log('[Decoder Worker] Decoder closed');
        } catch (error) {
          console.warn('[Decoder Worker] Decoder close error:', error);
        }
      }

      console.log('[Decoder Worker] Decoder stopped');

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
      console.error('[Decoder Worker] Failed to stop decoder:', error);
      throw error; // RPC automatically propagates errors
    }
  },

  /**
   * Decode an encoded chunk
   */
  decodeChunk: async (chunkData): Promise<void> => {
    if (!processing) {
      console.warn('[Decoder Worker] Dropping chunk - not processing');
      return;
    }

    const seq = chunkData.sequenceNumber ?? -1;
    // console.log(`[Decoder Worker] Received ${chunkData.type} chunk #${seq} via RPC, size: ${chunkData.byteLength}`);

    // If we're waiting for a keyframe due to packet loss, drop all non-keyframe chunks
    if (waitingForKeyframe && chunkData.type !== 'key') {
      // console.log(`[Decoder Worker] Dropping ${chunkData.type} chunk #${seq} while waiting for keyframe`);
      return;
    }

    // If this is a keyframe and we were waiting for one, reset recovery mode
    if (waitingForKeyframe && chunkData.type === 'key') {
      console.log(`[Decoder Worker] Recovery keyframe #${seq} received, resetting state`);
      waitingForKeyframe = false;
      reorderBuffer.clear();
      nextExpectedSequence = seq;
      // Process this keyframe immediately
      await decodeChunk(chunkData);
      nextExpectedSequence = seq + 1;
      return;
    }

    // Handle out-of-order delivery: buffer chunks until we can process in sequence
    if (seq !== -1 && seq !== nextExpectedSequence) {
      const gap = seq - nextExpectedSequence;
      console.log(`[Decoder Worker] Out-of-order chunk #${seq} (expecting #${nextExpectedSequence}), gap: ${gap}, buffering...`);
      reorderBuffer.set(seq, chunkData);

      // If gap is too large, likely we have packet loss
      if (gap >= MAX_REORDER_GAP) {
        console.warn(`[Decoder Worker] Gap of ${gap} detected, packet #${nextExpectedSequence} is likely lost`);

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
          console.log(`[Decoder Worker] Found keyframe #${firstKeyframeSeq} in buffer, skipping to it and discarding intermediate delta frames`);

          // Remove all chunks before the keyframe
          for (const [bufSeq] of reorderBuffer) {
            if (bufSeq < firstKeyframeSeq) {
              reorderBuffer.delete(bufSeq);
            }
          }

          // Jump to the keyframe sequence
          nextExpectedSequence = firstKeyframeSeq;
          await processBufferedChunks();
        } else {
          // No keyframe available - enter recovery mode
          console.warn(`[Decoder Worker] No keyframe in buffer after lost packet #${nextExpectedSequence}. Entering recovery mode - waiting for next keyframe.`);
          waitingForKeyframe = true;
          reorderBuffer.clear();
        }
        return;
      }

      // If we received a keyframe while waiting, we can reset and skip missing packets
      if (chunkData.type === 'key') {
        console.log(`[Decoder Worker] Received keyframe #${seq} while waiting for #${nextExpectedSequence}, resetting sequence`);
        nextExpectedSequence = seq;
        await decodeChunk(chunkData);
        nextExpectedSequence = seq + 1;
        // Clear old buffered chunks before this keyframe
        for (const [bufSeq] of reorderBuffer) {
          if (bufSeq < seq) {
            reorderBuffer.delete(bufSeq);
          }
        }
        await processBufferedChunks();
        return;
      }

      // Try to process buffered chunks in order
      await processBufferedChunks();
      return;
    }

    // Process this chunk immediately (it's in order)
    await decodeChunk(chunkData);

    // Increment and try to process next buffered chunks
    if (seq !== -1) {
      nextExpectedSequence = seq + 1;
      await processBufferedChunks();
    }
  },

  /**
   * Flush pending chunks
   */
  flush: async (): Promise<void> => {
    if (decoder) {
      try {
        await decoder.flush();
        console.log('[Decoder Worker] Decoder flushed');
      } catch (error) {
        console.warn('[Decoder Worker] Decoder flush error:', error);
      }
    }
  },

  /**
   * Get current decoder statistics
   */
  getStats: async (): Promise<DecoderStats> => {
    const decoderStats: DecoderStats = decoder?.getStats() || {
      decodedFrames: 0,
      droppedFrames: 0,
      averageDecodeTime: 0,
      hardwareAcceleration: 'unknown',
      resolution: 'N/A'
    };
    return decoderStats;
  },

  /**
   * Toggle between WASM and built-in decoders
   */
  toggleDecoderType: async (useWasm: boolean): Promise<void> => {
    try {
      console.log(`[Decoder Worker] Toggling decoder type to ${useWasm ? 'WASM' : 'built-in'}`);

      if (!decoder) {
        throw new Error('Decoder not initialized');
      }

      // For the regular decoder worker, we need to check if it supports toggling
      // Since WebCodecsDecoder doesn't have toggle functionality, we'll just log this
      console.log(`[Decoder Worker] Regular WebCodecs decoder doesn't support WASM/builtin toggling - using WebCodecs API`);

      // If this is an AV1 decoder, we could potentially switch to WASM implementation
      // But for now, we'll just log the request
      console.log(`[Decoder Worker] Decoder type toggle requested: ${useWasm ? 'WASM' : 'built-in'}`);
    } catch (error) {
      console.error('[Decoder Worker] Failed to toggle decoder type:', error);
      throw error;
    }
  }
};

// Initialize RPC communication (bidirectional)
callbacks = rpcClientServer<DecoderWorkerCallbacks>(
  'DecoderWorker',
  self as unknown as Worker,
  serverImpl
);

console.log('[Decoder Worker] Decoder worker initialized with RPC support');
