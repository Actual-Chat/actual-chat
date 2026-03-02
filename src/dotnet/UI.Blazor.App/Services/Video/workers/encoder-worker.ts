/**
 * Encoder Worker (Universal - Chrome & Safari)
 * Handles video encoding in a dedicated worker thread using RPC communication.
 * Receives VideoFrame objects and outputs serialized encoded chunks via RPC callbacks.
 * Chunk serialization (copyTo + description extraction) happens here in the worker,
 * keeping the main thread free from heavy sync operations.
 */

import { rpcClientServer, rpcNoWait } from 'rpc';
import { Log } from 'logging';

import { type EncoderConfig, type EncoderStats, WebCodecsEncoder } from '../webcodecs-encoder';
import type { EncoderWorker, EncoderWorkerCallbacks } from './encoder-worker-contract';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoEncoder');

// Worker state
let encoder: WebCodecsEncoder | null = null;
let processing = false;
let frameCount = 0;
let encoderConfig: EncoderConfig | null = null;
let resizeCanvas: OffscreenCanvas | null = null;
let resizeCtx: OffscreenCanvasRenderingContext2D | null = null;
let startTimestamp: number | undefined = undefined;

/**
 * Detect if description bytes are in avcC (H.264 decoder configuration record) format.
 * Used to detect when encoder silently falls back to H.264 despite being configured for AV1.
 */
function isAvcCDescription(desc: ArrayBuffer): boolean {
    if (desc.byteLength < 5) return false;
    const bytes = new Uint8Array(desc);
    // configurationVersion must be 1
    if (bytes[0] !== 0x01) return false;
    // profileIndication must be a known H.264 profile
    const validProfiles = [66, 77, 88, 100, 110, 122, 244];
    if (!validProfiles.includes(bytes[1])) return false;
    // byte[4] reserved bits: upper 6 bits must be 1 (0xFC mask)
    if ((bytes[4] & 0xFC) !== 0xFC) return false;
    return true;
}

/**
 * Derive avc1 codec string from avcC description bytes.
 */
function deriveAvcCodecFromDescription(desc: ArrayBuffer): string {
    const bytes = new Uint8Array(desc);
    const profile = bytes[1].toString(16).padStart(2, '0');
    const compat = bytes[2].toString(16).padStart(2, '0');
    const level = bytes[3].toString(16).padStart(2, '0');
    return `avc1.${profile}${compat}${level}`;
}

// Resize frame to match encoder dimensions while preserving aspect ratio
function resizeFrame(frame: VideoFrame, targetWidth: number, targetHeight: number): VideoFrame {
    const frameWidth = frame.displayWidth;
    const frameHeight = frame.displayHeight;

    // If dimensions match, no resize needed
    if (frameWidth === targetWidth && frameHeight === targetHeight) {
        return frame;
    }

    // Create canvas if needed
    if (resizeCanvas?.width !== targetWidth || resizeCanvas.height !== targetHeight) {
        infoLog?.log(`Creating resize canvas: ${targetWidth}x${targetHeight}`);
        resizeCanvas = new OffscreenCanvas(targetWidth, targetHeight);
        resizeCtx = resizeCanvas.getContext('2d', { willReadFrequently: true });
    }

    if (!resizeCtx) {
    // Fallback - return original frame
        warnLog?.log('Could not create 2D context for resizing');
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

    // Create new VideoFrame from canvas (timestamp normalization happens in encodeFrame)
    const newFrame = new VideoFrame(resizeCanvas, {
        timestamp: frame.timestamp,
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
            infoLog?.log('Initializing encoder...');

            // Store encoder config for frame resizing
            encoderConfig = config;

            encoder = new WebCodecsEncoder(
                config,
                (chunkData) => {
                    // Serialize chunk in worker: copyTo + description extraction
                    // This keeps the main thread free from sync ArrayBuffer operations

                    // 1. Copy encoded chunk bytes
                    const chunkBuffer = new ArrayBuffer(chunkData.byteLength);
                    chunkData.chunk.copyTo(new Uint8Array(chunkBuffer));

                    // 2. Validate actual codec output: if configured for AV1 but output is avcC, correct it
                    let actualCodec = encoderConfig!.codec;
                    let descBuffer: ArrayBuffer | undefined;

                    if (chunkData.type === 'key' && chunkData.metadata?.decoderConfig?.description) {
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

                        descBuffer = new ArrayBuffer(sourceArray.byteLength);
                        new Uint8Array(descBuffer).set(sourceArray);

                        debugLog?.log('Keyframe metadata serialized, description size:', descBuffer.byteLength);

                        // Check for codec mismatch
                        if (isAvcCDescription(descBuffer) && !encoderConfig!.codec.startsWith('avc1')) {
                            const derivedCodec = deriveAvcCodecFromDescription(descBuffer);
                            warnLog?.log(`Encoder output mismatch: configured=${encoderConfig!.codec} but output is avcC, correcting to ${derivedCodec}`);
                            actualCodec = derivedCodec;
                            encoderConfig!.codec = derivedCodec;
                        }
                    }

                    // 3. Send serialized data to main thread via RPC (zero-copy transfer of ArrayBuffers)
                    void callbacks.onSerializedChunk(
                        chunkBuffer,
                        chunkData.chunk.timestamp,
                        chunkData.chunk.duration ?? 0,
                        chunkData.type === 'key',
                        actualCodec,
                        chunkData.sequenceNumber,
                        descBuffer,
                        rpcNoWait
                    );
                },
                (error) => {
                    errorLog?.log('Encoder error:', error);
                }
            );

            encoder.initialize();

            // Mark as ready to process frames
            processing = true;
            frameCount = 0;

            infoLog?.log('Ready to encode frames');
        } catch (error) {
            errorLog?.log('Failed to initialize encoder:', error);
            throw error; // RPC automatically propagates errors
        }
    },

    /**
   * Stop the encoder
   */
    stop: async (): Promise<void> => {
        try {
            infoLog?.log('Stopping encoder...');

            processing = false;

            // Flush and close encoder
            if (encoder) {
                try {
                    await encoder.flush();
                    encoder.close();
                    infoLog?.log('Encoder closed');
                } catch (error) {
                    warnLog?.log('Encoder close error:', error);
                }
            }

            infoLog?.log('Encoder stopped');

            // Reset state
            encoder = null;
            encoderConfig = null;
            resizeCanvas = null;
            resizeCtx = null;
            frameCount = 0;
            startTimestamp = undefined;
        } catch (error) {
            errorLog?.log('Failed to stop encoder:', error);
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

        // Backpressure: drop frame if encoder queue is building up
        if (encoder.getEncodeQueueSize() > 3) {
            frame.close();
            debugLog?.log('Frame dropped due to encoder backpressure (queueSize > 3)');
            return;
        }

        try {
            // Record start timestamp for normalization
            if (startTimestamp === undefined) {
                startTimestamp = frame.timestamp;
                infoLog?.log(`Start timestamp set to ${startTimestamp}μs`);
            }

            // Resize frame if dimensions don't match encoder configuration
            let processedFrame = resizeFrame(frame, encoderConfig.width, encoderConfig.height);

            // Normalize timestamp to 0-based (relative to first frame).
            // resizeFrame may or may not return a new frame, so we always normalize here.
            const normalizedTs = processedFrame.timestamp - startTimestamp;
            if (normalizedTs !== processedFrame.timestamp) {
                const normalized = new VideoFrame(processedFrame, {
                    timestamp: normalizedTs,
                    duration: processedFrame.duration ?? undefined,
                });
                processedFrame.close();
                processedFrame = normalized;
            }

            // Encode frame (keyframe every 30 frames for 1 second at 30fps)
            const isKeyFrame = frameCount % 30 === 0;
            encoder.encode(processedFrame, isKeyFrame);
            frameCount++;
        } catch (error) {
            errorLog?.log('Error processing frame:', error);
            try { frame.close(); } catch { /* already closed */ }
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
                infoLog?.log('Encoder flushed');
            } catch (error) {
                warnLog?.log('Encoder flush error:', error);
            }
        }
    },

    /**
   * Reconfigure encoder with new parameters
   */
    reconfigure: async (params): Promise<void> => {
        if (!encoder || !processing || !encoderConfig) {
            warnLog?.log('Cannot reconfigure: encoder not active');
            return;
        }

        try {
            infoLog?.log(`Reconfigure request: ${params.bitrate / 1_000_000}Mbps, ${params.width}x${params.height}`);

            // Update stored config
            encoderConfig.bitrate = params.bitrate;
            encoderConfig.width = params.width;
            encoderConfig.height = params.height;

            // Reconfigure encoder
            await encoder.reconfigure({
                bitrate: params.bitrate,
                width: params.width,
                height: params.height
            });

            // Clear resize canvas to force recreation with new dimensions
            resizeCanvas = null;
            resizeCtx = null;

            infoLog?.log('Encoder reconfigured successfully');
        } catch (error) {
            errorLog?.log('Failed to reconfigure encoder:', error);
            throw error; // RPC automatically propagates errors
        }
    },

    /**
   * Switch codec: flush and close current encoder, create and configure new encoder
   */
    switchCodec: async (config: EncoderConfig): Promise<void> => {
        if (!encoder) {
            warnLog?.log('Cannot switch codec: encoder not active');
            return;
        }

        try {
            infoLog?.log(`Switching codec to ${config.codec}`);

            // Switch codec in the encoder (flush + close old, create + configure new)
            await encoder.switchCodec(config);

            // Update stored config
            encoderConfig = config;

            // Clear resize canvas (force recreation at potentially new dimensions)
            resizeCanvas = null;
            resizeCtx = null;

            // Reset frame counter and start timestamp for fresh keyframe scheduling
            frameCount = 0;
            startTimestamp = undefined;

            infoLog?.log('Codec switched successfully');
        } catch (error) {
            errorLog?.log('Failed to switch codec:', error);
            throw error;
        }
    },

    /**
   * Force the next encoded frame to be a keyframe
   */
    // eslint-disable-next-line
    forceKeyFrame: async (): Promise<void> => {
        frameCount = 0; // Next encode will be keyframe (frameCount % 30 === 0)
        infoLog?.log('Forced next frame to be keyframe');
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

infoLog?.log('Encoder worker initialized');
