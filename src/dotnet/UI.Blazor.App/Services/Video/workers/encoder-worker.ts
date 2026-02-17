/**
 * Encoder Worker (Universal - Chrome & Safari)
 * Handles video encoding in a dedicated worker thread using RPC communication.
 * Receives VideoFrame objects and outputs encoded chunks via RPC callbacks.
 */

import { rpcClientServer, rpcNoWait } from 'rpc';

import { type EncoderConfig, type EncoderStats, WebCodecsEncoder } from '../webcodecs-encoder';
import type { EncoderWorker, EncoderWorkerCallbacks } from './encoder-worker-contract';

// Worker state
let encoder: WebCodecsEncoder | null = null;
let processing = false;
let frameCount = 0;
let encoderConfig: EncoderConfig | null = null;
let resizeCanvas: OffscreenCanvas | null = null;
let resizeCtx: OffscreenCanvasRenderingContext2D | null = null;
let startTimestamp: number | undefined = undefined;

// Resize frame to match encoder dimensions while preserving aspect ratio
function resizeFrame(frame: VideoFrame, targetWidth: number, targetHeight: number): VideoFrame {
    const frameWidth = frame.displayWidth;
    const frameHeight = frame.displayHeight;

    // If dimensions match, no resize needed
    if (frameWidth === targetWidth && frameHeight === targetHeight) {
        return frame;
    }

    // console.log(`[Encoder Worker] 📐 Resizing frame from ${frameWidth}x${frameHeight} to ${targetWidth}x${targetHeight} for encoding`);

    // Create canvas if needed
    if (resizeCanvas?.width !== targetWidth || resizeCanvas.height !== targetHeight) {
        console.log(`[Encoder Worker] Creating new resize canvas: ${targetWidth}x${targetHeight}`);
        resizeCanvas = new OffscreenCanvas(targetWidth, targetHeight);
        resizeCtx = resizeCanvas.getContext('2d', { willReadFrequently: true });
    }

    if (!resizeCtx) {
    // Fallback - return original frame
        console.warn('[Encoder Worker] Could not create 2D context for resizing');
        return frame;
    }

    // Clear canvas to black (letterboxing/pillarboxing)
    resizeCtx.fillStyle = '#000000';
    resizeCtx.fillRect(0, 0, targetWidth, targetHeight);

    // Calculate aspect ratios
    const frameAspect = frameWidth / frameHeight;
    const targetAspect = targetWidth / targetHeight;

    let drawWidth: number;
    let drawHeight: number;
    let offsetX: number;
    let offsetY: number;

    if (frameAspect > targetAspect) {
    // Frame is wider - fit to width, letterbox top/bottom
        drawWidth = targetWidth;
        drawHeight = targetWidth / frameAspect;
        offsetX = 0;
        offsetY = (targetHeight - drawHeight) / 2;
    } else {
    // Frame is taller - fit to height, pillarbox left/right
        drawHeight = targetHeight;
        drawWidth = targetHeight * frameAspect;
        offsetX = (targetWidth - drawWidth) / 2;
        offsetY = 0;
    }

    // Draw frame centered with aspect ratio preserved
    resizeCtx.drawImage(frame, offsetX, offsetY, drawWidth, drawHeight);

    // Create new VideoFrame from canvas
    const newFrame = new VideoFrame(resizeCanvas, {
        timestamp: frame.timestamp - (startTimestamp ?? 0),
        duration: frame.duration ?? undefined
    });

    // Close original frame
    frame.close();

    return newFrame;
}

// RPC Server Implementation
const serverImpl: EncoderWorker = {
    /**
   * Initialize the encoder
   */
    // eslint-disable-next-line
    initialize: async (config): Promise<void> => {
        try {
            console.log('[Encoder Worker] Initializing encoder via RPC...');

            // Store encoder config for frame resizing
            encoderConfig = config;

            encoder = new WebCodecsEncoder(
                config,
                (chunkData) => {
                    // Serialize metadata properly (it contains ArrayBuffer which needs special handling)
                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    let serializedMetadata: any = undefined;
                    if (chunkData.metadata?.decoderConfig?.description) {
                        const desc = chunkData.metadata.decoderConfig.description;
                        let sourceArray: Uint8Array;
                        if (desc instanceof ArrayBuffer) {
                            sourceArray = new Uint8Array(desc);
                        } else if (desc instanceof SharedArrayBuffer) {
                            sourceArray = new Uint8Array(desc);
                        } else if (ArrayBuffer.isView(desc)) {
                            sourceArray = new Uint8Array(desc.buffer, desc.byteOffset, desc.byteLength);
                        } else {
                            sourceArray = new Uint8Array(desc as ArrayBuffer);
                        }

                        const descBuffer = new ArrayBuffer(sourceArray.byteLength);
                        new Uint8Array(descBuffer).set(sourceArray);

                        serializedMetadata = {
                            decoderConfig: {
                                ...chunkData.metadata.decoderConfig,
                                description: descBuffer
                            }
                        };

                        if (chunkData.type === 'key') {
                            console.log(`[Encoder Worker] Keyframe metadata serialized, description size: ${descBuffer.byteLength}`);
                        }
                    }

                    const enrichedChunkData = {
                        ...chunkData,
                        // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
                        metadata: serializedMetadata,
                        sequenceNumber: chunkData.sequenceNumber
                    };

                    void callbacks.onEncodedChunk(enrichedChunkData, rpcNoWait);
                },
                (error) => {
                    console.error('[Encoder Worker] Encoder error:', error);
                    // Errors during encoding will propagate through encodeFrame() promise rejection
                }
            );

            encoder.initialize();
            // console.log('[Encoder Worker] Encoder initialized via RPC');

            // Mark as ready to process frames
            processing = true;
            frameCount = 0;

            console.log('[Encoder Worker] Ready to encode frames');
            // No callback needed - initialize() returning successfully means ready
        } catch (error) {
            console.error('[Encoder Worker] Failed to initialize encoder:', error);
            throw error; // RPC automatically propagates errors
        }
    },

    /**
   * Stop the encoder
   */
    stop: async (): Promise<void> => {
        try {
            console.log('[Encoder Worker] Stopping encoder via RPC...');

            processing = false;

            // Flush and close encoder
            if (encoder) {
                try {
                    await encoder.flush();
                    encoder.close();
                    console.log('[Encoder Worker] Encoder closed');
                } catch (error) {
                    console.warn('[Encoder Worker] Encoder close error:', error);
                }
            }

            console.log('[Encoder Worker] Encoder stopped');

            // Reset state
            encoder = null;
            encoderConfig = null;
            resizeCanvas = null;
            resizeCtx = null;
            frameCount = 0;
            startTimestamp = undefined;
        } catch (error) {
            console.error('[Encoder Worker] Failed to stop encoder:', error);
            throw error; // RPC automatically propagates errors
        }
    },

    /**
   * Encode a single frame
   */
    // eslint-disable-next-line
    encodeFrame: async (frame): Promise<void> => {
        if (!encoder || !processing || !encoderConfig) {
            frame.close();
            return;
        }

        try {
            // Record start timestamp for normalization
            if (startTimestamp === undefined) {
                startTimestamp = frame.timestamp;
                console.log(`[Encoder Worker] Start timestamp set to ${startTimestamp}μs`);
            }

            // Resize frame if dimensions don't match encoder configuration
            const processedFrame = resizeFrame(frame, encoderConfig.width, encoderConfig.height);

            // Encode frame (keyframe every 30 frames for 1 second at 30fps)
            const isKeyFrame = frameCount % 30 === 0;
            encoder.encode(processedFrame, isKeyFrame);
            frameCount++;
        } catch (error) {
            console.error('[Encoder Worker] Error processing frame:', error);
            frame.close();
            throw error; // RPC automatically propagates errors
        }
    },

    /**
   * Flush pending frames
   */
    flush: async (): Promise<void> => {
        if (encoder) {
            try {
                await encoder.flush();
                console.log('[Encoder Worker] Encoder flushed');
            } catch (error) {
                console.warn('[Encoder Worker] Encoder flush error:', error);
            }
        }
    },

    /**
   * Reconfigure encoder with new parameters
   */
    reconfigure: async (params): Promise<void> => {
        if (!encoder || !processing || !encoderConfig) {
            console.warn('[Encoder Worker] Cannot reconfigure: encoder not active');
            return;
        }

        try {
            console.log(`[Encoder Worker] 🔧 RECONFIGURE REQUEST via RPC: ${params.bitrate / 1_000_000}Mbps, ${params.width}x${params.height}`);
            console.log('[Encoder Worker] Current frame count:', frameCount);

            const oldBitrate = encoderConfig.bitrate;
            const oldWidth = encoderConfig.width;
            const oldHeight = encoderConfig.height;

            // Update stored config
            encoderConfig.bitrate = params.bitrate;
            encoderConfig.width = params.width;
            encoderConfig.height = params.height;

            console.log('[Encoder Worker] Config changed from:',
                `${oldBitrate / 1_000_000}Mbps ${oldWidth}x${oldHeight}`,
                'to:',
                `${encoderConfig.bitrate / 1_000_000}Mbps ${encoderConfig.width}x${encoderConfig.height}`
            );

            // Reconfigure encoder
            await encoder.reconfigure({
                bitrate: params.bitrate,
                width: params.width,
                height: params.height
            });

            // Clear resize canvas to force recreation with new dimensions
            resizeCanvas = null;
            resizeCtx = null;

            console.log('[Encoder Worker] ✅ Encoder reconfigured successfully via RPC');
            console.log('[Encoder Worker] 📊 Next keyframe will occur naturally based on encoding logic');
        } catch (error) {
            console.error('[Encoder Worker] Failed to reconfigure encoder:', error);
            throw error; // RPC automatically propagates errors
        }
    },

    /**
   * Get current encoder statistics
   */
    // eslint-disable-next-line
    getStats: async (): Promise<EncoderStats> => {
        return encoder?.getStats() ?? {
            encodedFrames: 0,
            droppedFrames: 0,
            keyFrames: 0,
            totalBytes: 0,
            averageEncodeTime: 0,
            hardwareAcceleration: 'unknown'
        };
    },
};

// Initialize RPC communication (bidirectional)
const callbacks = rpcClientServer<EncoderWorkerCallbacks>(
    'EncoderWorker',
  self as unknown as Worker,
  serverImpl
);

console.log('[Encoder Worker] Encoder worker initialized with RPC support');
