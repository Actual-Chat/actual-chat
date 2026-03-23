/**
 * Encoder Worker (Universal - Chrome & Safari)
 * Handles video encoding in a dedicated worker thread using RPC communication.
 * Receives VideoFrame objects and outputs serialized encoded chunks via RPC callbacks.
 * Chunk serialization (copyTo + description extraction) happens here in the worker,
 * keeping the main thread free from heavy sync operations.
 */

import { rpcClientServer, rpcNoWait } from 'rpc';
import { Log } from 'logging';
import { DeviceInfo } from 'device-info';

import { type EncoderConfig, type EncoderStats, WebCodecsEncoder } from '../webcodecs-encoder';
import type { EncoderWorker, EncoderWorkerCallbacks } from './encoder-worker-contract';
import type { SerializedChunkMessage } from './stream-channel';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoEncoder');

// Worker state
let encoder: WebCodecsEncoder | null = null;
let processing = false;
let frameCount = 0;
let encoderConfig: EncoderConfig | null = null;
let resizeCanvas: OffscreenCanvas | null = null;
let resizeCtx: OffscreenCanvasRenderingContext2D | null = null;
let startTimestamp: number | undefined = undefined;
let lastLoggedFormat: string | null = '(unset)';
let loggedI420Error = false;

// Backpressure tracking
let backpressureDrops = 0;
let backpressureTotalFrames = 0;
let lastBackpressureCheckTime = 0;
const backpressureWindowMs = 5000;       // evaluation window
const backpressureDropThreshold = 0.20;  // 20% drop rate triggers notification
let backpressureNotified = false;        // avoid repeated notifications

// Stream-based output: when set, encoded chunks are written here instead of RPC callback
let streamOutputWriter: WritableStreamDefaultWriter<SerializedChunkMessage> | null = null;
// Stream-based input reader loop promise (for cleanup)
let streamReadLoopPromise: Promise<void> | null = null;

// VAD state for stream mode (communicated via RPC from main thread)
let vadSpeaking = true;
let vadRemoteStreamCount = 0;
let vadReducedFrameIntervalMs = 1000 / 5; // default 5fps when silent
let vadLastPassedFrameTime = 0;

// Dimension reconciliation state for stream mode
let dimensionsReconciled = false;

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

/**
 * CPU fallback: convert a non-YUV VideoFrame to I420 by drawing to a canvas
 * and manually applying BT.601 limited-range conversion.
 * Only used when native copyTo({ format: 'I420' }) fails on all formats.
 */
function cpuRgbaToI420(frame: VideoFrame): VideoFrame {
    const w = frame.codedWidth;
    const h = frame.codedHeight;

    // Draw frame to canvas to get RGBA pixels
    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
    if (resizeCanvas?.width !== w || resizeCanvas?.height !== h) {
        resizeCanvas = new OffscreenCanvas(w, h);
        resizeCtx = resizeCanvas.getContext('2d', { willReadFrequently: true });
    }
    resizeCtx!.drawImage(frame, 0, 0, w, h);
    const imageData = resizeCtx!.getImageData(0, 0, w, h);
    const rgba = imageData.data;

    const chromaW = Math.ceil(w / 2);
    const chromaH = Math.ceil(h / 2);
    const ySize = w * h;
    const uvSize = chromaW * chromaH;
    const i420Buf = new ArrayBuffer(ySize + uvSize * 2);
    const out = new Uint8Array(i420Buf);

    // BT.601 limited-range Y
    for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
            const i = (y * w + x) * 4;
            const r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
            out[y * w + x] = Math.max(0, Math.min(255, Math.round(
                65.481 * r / 255 + 128.553 * g / 255 + 24.966 * b / 255 + 16
            )));
        }
    }

    // BT.601 limited-range U and V (2×2 subsampling)
    for (let cy = 0; cy < chromaH; cy++) {
        for (let cx = 0; cx < chromaW; cx++) {
            let rSum = 0, gSum = 0, bSum = 0, count = 0;
            for (let dy = 0; dy < 2; dy++) {
                for (let dx = 0; dx < 2; dx++) {
                    const px = Math.min(cx * 2 + dx, w - 1);
                    const py = Math.min(cy * 2 + dy, h - 1);
                    const i = (py * w + px) * 4;
                    rSum += rgba[i];
                    gSum += rgba[i + 1];
                    bSum += rgba[i + 2];
                    count++;
                }
            }
            const r = rSum / count / 255;
            const g = gSum / count / 255;
            const b = bSum / count / 255;
            const chromaIdx = cy * chromaW + cx;
            out[ySize + chromaIdx] = Math.max(0, Math.min(255, Math.round(
                -37.797 * r - 74.203 * g + 112.0 * b + 128
            )));
            out[ySize + uvSize + chromaIdx] = Math.max(0, Math.min(255, Math.round(
                112.0 * r - 93.786 * g - 18.214 * b + 128
            )));
        }
    }

    const yStride = w;
    const uvStride = chromaW;
    const i420Frame = new VideoFrame(i420Buf, {
        format: 'I420',
        codedWidth: w,
        codedHeight: h,
        timestamp: frame.timestamp,
        duration: frame.duration ?? undefined,
        layout: [
            { offset: 0, stride: yStride },
            { offset: ySize, stride: uvStride },
            { offset: ySize + uvSize, stride: uvStride },
        ],
    });
    frame.close();
    return i420Frame;
}

/**
 * Serialize an encoded chunk and emit it to the appropriate output (stream or RPC).
 * Extracted from initialize() to be shared between RPC and stream modes.
 */
function onEncodedChunk(chunkData: import('../webcodecs-encoder').EncodedChunkData): void {
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

    // 3. Emit serialized data
    if (streamOutputWriter) {
        // Stream mode: write to output stream
        void streamOutputWriter.write({
            chunkBytes: chunkBuffer,
            timestamp: chunkData.chunk.timestamp,
            duration: chunkData.chunk.duration ?? 0,
            isKeyFrame: chunkData.type === 'key',
            codec: actualCodec,
            sequenceNumber: chunkData.sequenceNumber,
            descriptionBytes: descBuffer,
        });
    } else {
        // RPC fallback: send via RPC callback
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
    }
}

/**
 * Create and configure the WebCodecsEncoder with the shared onEncodedChunk handler.
 */
function createEncoder(config: EncoderConfig): void {
    encoderConfig = config;
    encoder = new WebCodecsEncoder(
        config,
        onEncodedChunk,
        (error) => {
            errorLog?.log('Encoder error:', error);
        }
    );
    encoder.initialize();
}

/**
 * Stream read loop: reads VideoFrames from transferred ReadableStream,
 * handles dimension reconciliation, VAD-based dropping, then encodes.
 */
async function streamReadLoop(inputReader: ReadableStreamDefaultReader<VideoFrame>): Promise<void> {
    try {
        while (processing) {
            const { done, value: rawFrame } = await inputReader.read();
            if (done) {
                infoLog?.log('Stream input ended');
                break;
            }

            if (!encoder || !processing || !encoderConfig) {
                rawFrame.close();
                continue;
            }

            // Dimension reconciliation on first frame (stream mode)
            if (!dimensionsReconciled) {
                dimensionsReconciled = true;
                const frameW = rawFrame.codedWidth;
                const frameH = rawFrame.codedHeight;
                if (frameW !== encoderConfig.width || frameH !== encoderConfig.height) {
                    warnLog?.log(`Frame dimensions ${frameW}x${frameH} differ from encoder config ${encoderConfig.width}x${encoderConfig.height}, reconfiguring`);
                    encoderConfig.width = frameW;
                    encoderConfig.height = frameH;
                    await encoder.reconfigure({ width: frameW, height: frameH, bitrate: encoderConfig.bitrate });
                    // Notify main thread
                    void callbacks.onDimensionReconciled(frameW, frameH, rpcNoWait);
                }
            }

            // Normalize display dimensions (Android MSTP issue)
            let frame = rawFrame;
            if (rawFrame.displayWidth !== rawFrame.codedWidth
                || rawFrame.displayHeight !== rawFrame.codedHeight) {
                frame = new VideoFrame(rawFrame, {
                    displayWidth: rawFrame.codedWidth,
                    displayHeight: rawFrame.codedHeight,
                });
                rawFrame.close();
            }

            // VAD-based adaptive framerate: drop frames when not speaking (group calls)
            if (!vadSpeaking && vadRemoteStreamCount >= 2) {
                const now = performance.now();
                if (now - vadLastPassedFrameTime < vadReducedFrameIntervalMs) {
                    frame.close();
                    continue;
                }
                vadLastPassedFrameTime = now;
            }

            // Delegate to the existing encodeFrame logic (handles backpressure, resize, YUV, encode)
            await serverImpl.encodeFrame(frame);
        }
    } catch (error) {
        if (processing) {
            errorLog?.log('Stream read error:', error);
        }
    } finally {
        try { inputReader.releaseLock(); } catch { /* ignore */ }
    }
}

// RPC Server Implementation
const serverImpl: EncoderWorker = {
    /**
   * Initialize the encoder (RPC fallback path)
   */
    // eslint-disable-next-line
    initialize: async (config): Promise<void> => {
        try {
            infoLog?.log('Initializing encoder (RPC mode)...');

            createEncoder(config);

            // Mark as ready to process frames
            processing = true;
            frameCount = 0;

            infoLog?.log('Ready to encode frames (RPC mode)');
        } catch (error) {
            errorLog?.log('Failed to initialize encoder:', error);
            throw error;
        }
    },

    /**
   * Initialize and start stream-based encoding.
   */
    startWithStream: async (
        config: EncoderConfig,
        frameInputStream: ReadableStream<VideoFrame>,
        chunkOutputStream: WritableStream<SerializedChunkMessage>,
    ): Promise<void> => {
        try {
            infoLog?.log('Initializing encoder (stream mode)...');

            // Set stream output writer — onEncodedChunk will write to stream instead of RPC
            streamOutputWriter = chunkOutputStream.getWriter();

            createEncoder(config);

            // Mark as ready to process frames
            processing = true;
            frameCount = 0;
            dimensionsReconciled = false;

            // Start reading from input stream (async, runs in background)
            const inputReader = frameInputStream.getReader();
            streamReadLoopPromise = streamReadLoop(inputReader);

            infoLog?.log('Ready to encode frames (stream mode)');
        } catch (error) {
            errorLog?.log('Failed to initialize encoder stream mode:', error);
            throw error;
        }
    },

    /**
   * Update VAD state for adaptive framerate in stream mode.
   */
    // eslint-disable-next-line
    setVadState: async (speaking: boolean, remoteStreamCount: number): Promise<void> => {
        vadSpeaking = speaking;
        vadRemoteStreamCount = remoteStreamCount;
        if (speaking) {
            vadLastPassedFrameTime = 0; // allow next frame through immediately
        }
        debugLog?.log(`VAD state: speaking=${speaking}, remoteStreamCount=${remoteStreamCount}`);
    },

    /**
   * Stop the encoder
   */
    stop: async (): Promise<void> => {
        try {
            infoLog?.log('Stopping encoder...');

            processing = false;

            // Wait for stream read loop to finish
            if (streamReadLoopPromise) {
                try { await streamReadLoopPromise; } catch { /* ignore */ }
                streamReadLoopPromise = null;
            }

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

            // Close stream output writer
            if (streamOutputWriter) {
                try { await streamOutputWriter.close(); } catch { /* ignore */ }
                streamOutputWriter = null;
            }

            infoLog?.log('Encoder stopped');

            // Reset state
            encoder = null;
            encoderConfig = null;
            resizeCanvas = null;
            resizeCtx = null;
            frameCount = 0;
            startTimestamp = undefined;
            backpressureDrops = 0;
            backpressureTotalFrames = 0;
            lastBackpressureCheckTime = 0;
            backpressureNotified = false;
            dimensionsReconciled = false;
            vadSpeaking = true;
            vadRemoteStreamCount = 0;
            vadLastPassedFrameTime = 0;
        } catch (error) {
            errorLog?.log('Failed to stop encoder:', error);
            throw error;
        }
    },

    /**
   * Encode a single frame
   */
    encodeFrame: async (frame): Promise<void> => {
        if (!encoder || !processing || !encoderConfig) {
            frame.close();
            return;
        }

        // Backpressure: drop frame if encoder queue is building up.
        // iOS has stricter thread scheduling — use tighter threshold to avoid starving audio.
        const backpressureMaxQueue = DeviceInfo.isIos ? 1 : 3;
        backpressureTotalFrames++;
        if (encoder.getEncodeQueueSize() > backpressureMaxQueue) {
            frame.close();
            backpressureDrops++;
            debugLog?.log(`Frame dropped due to encoder backpressure (queueSize > ${backpressureMaxQueue})`);

            // Check if we should notify main thread about sustained backpressure
            const now = performance.now();
            if (now - lastBackpressureCheckTime > backpressureWindowMs) {
                const dropRate = backpressureDrops / backpressureTotalFrames;
                if (dropRate > backpressureDropThreshold && !backpressureNotified) {
                    backpressureNotified = true;
                    warnLog?.log(`Sustained backpressure: dropRate=${(dropRate * 100).toFixed(1)}% ` +
                        `(${backpressureDrops}/${backpressureTotalFrames} in ${backpressureWindowMs}ms)`);
                    void callbacks.onBackpressure(dropRate, rpcNoWait);
                }
                // Reset counters for next window
                backpressureDrops = 0;
                backpressureTotalFrames = 0;
                lastBackpressureCheckTime = now;
            }
            return;
        }

        // Reset backpressure notification flag when encoding succeeds consistently
        if (backpressureNotified && backpressureTotalFrames > 30) {
            const now = performance.now();
            if (now - lastBackpressureCheckTime > backpressureWindowMs) {
                const dropRate = backpressureDrops / backpressureTotalFrames;
                if (dropRate < 0.05) {
                    backpressureNotified = false;
                    debugLog?.log('Backpressure resolved');
                }
                backpressureDrops = 0;
                backpressureTotalFrames = 0;
                lastBackpressureCheckTime = now;
            }
        }

        try {
            // Record start timestamp for normalization
            if (startTimestamp === undefined) {
                startTimestamp = frame.timestamp;
                infoLog?.log(`Start timestamp set to ${startTimestamp}μs`);
            }

            // Resize frame if dimensions don't match encoder configuration
            let processedFrame = resizeFrame(frame, encoderConfig.width, encoderConfig.height);

            // On mobile, hardware encoders expect NV12/I420 but canvas/blur gives RGBA/BGRA.
            // Try native format conversion (browser's optimized libyuv), with fallback chain.
            // Note: VideoFrame.format is null for frames from canvas sources (WebGPU, 2D),
            // so we convert any frame that's NOT already in a YUV format.
            const format = processedFrame.format as string | null;
            const isAlreadyYuv = format === 'NV12' || format === 'I420'
                || format === 'I420A' || format === 'I422' || format === 'I444'
                || format === 'NV12A';
            if (!isAlreadyYuv) {
                let converted = false;
                // Try native formats in order of preference
                for (const fmt of ['I420', 'NV12', 'I420A', 'I422'] as const) {
                    try {
                        const size = processedFrame.allocationSize({ format: fmt });
                        const buf = new ArrayBuffer(size);
                        const layout = await processedFrame.copyTo(buf, { format: fmt });
                        const yuvFrame = new VideoFrame(buf, {
                            format: fmt,
                            codedWidth: processedFrame.codedWidth,
                            codedHeight: processedFrame.codedHeight,
                            timestamp: processedFrame.timestamp,
                            duration: processedFrame.duration ?? undefined,
                            layout,
                            colorSpace: processedFrame.colorSpace,
                        });
                        processedFrame.close();
                        processedFrame = yuvFrame;
                        converted = true;
                        break;
                    } catch { continue; }
                }

                // CPU manual RGBA→I420 fallback if all native formats fail
                if (!converted) {
                    try {
                        processedFrame = cpuRgbaToI420(processedFrame);
                    } catch (e) {
                        if (!loggedI420Error) {
                            loggedI420Error = true;
                            warnLog?.log('All YUV conversion methods failed:', String(e));
                        }
                    }
                }
            }

            // Log frame format for diagnostics (after I420 conversion, so it shows actual encoded format)
            if (processedFrame.format !== lastLoggedFormat) {
                lastLoggedFormat = processedFrame.format;
                warnLog?.log(`Frame format: ${processedFrame.format}, ${processedFrame.codedWidth}x${processedFrame.codedHeight}`);
            }

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
            backpressureDrops = 0;
            backpressureTotalFrames = 0;
            lastBackpressureCheckTime = 0;
            backpressureNotified = false;

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
            medianEncodeTime: 0,
            pureMedianEncodeTime: -1,
            configuredWidth: 0,
            configuredHeight: 0,
            configuredBitrate: 0,
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
