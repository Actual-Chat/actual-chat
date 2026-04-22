/**
 * Video processing implementation.
 * Core logic for segmentation, encoding, and streaming — used by video-processing-worker.ts.
 */

import { rpcNoWait } from 'rpc';
import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import Denque from 'denque';
import * as ort from 'onnxruntime-web';

import { type EncoderConfig, type EncodedChunkData, WebCodecsEncoder } from '../webcodecs-encoder';
import type { SegmentationConfig, SegmentationStats, ModelConfig, VideoProcessingConfig, VideoProcessingWorker, VideoProcessingWorkerCallbacks, VideoProcessingStats, OrientationStats } from './video-processing-worker-contract';
import { getModelConfig } from './video-processing-worker-contract';
import {
    initTensorWebGPU,
    processDeferredCleanups,
    returnPooledBuffer,
    videoFrameToTensorFloat32,
    videoFrameToTensorUint8,
} from '../tensor-utils';
import {
    applyBackgroundBlur,
    submitBlurI420,
    awaitAllPendingReadbacks,
    applyTemporalSmoothing,
    initBlurWebGPU,
    processBlurDeferredCleanups,
} from '../webgpu-blur';
import { WebGPUManager } from '../webgpu-manager';
import { WebGpuDownscaler } from '../webgpu-downscaler';

import { isAvcCDescription, deriveAvcCodecFromDescription, resizeFrame, cpuRgbaToI420 } from './video-encoding-helpers';
import {
    type VideoStreamFrame, type StreamingContext,
    microsecondsToTicks, InternalVideoStream,
} from './video-streaming';
import { Api } from 'api';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';

// Import the ONNX model so esbuild copies it to dist/assets/onnx/
import SegmentationModelUrl from './selfie_segmentation_olive_webgpu.onnx';

// Type declaration for Insertable Streams API (may be available in worker scope on Safari 18+)
declare class MediaStreamTrackProcessor<T = VideoFrame> {
    constructor(options: { track: MediaStreamTrack });
    readable: ReadableStream<T>;
}

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

// ─── Callbacks (set by worker entry after RPC init) ─────────────────────────

let callbacks: VideoProcessingWorkerCallbacks;

export function setCallbacks(cb: VideoProcessingWorkerCallbacks): void {
    callbacks = cb;
}

// ─── State ──────────────────────────────────────────────────────────────────

// Encoder
let encoder: WebCodecsEncoder | null = null;
let encoderConfig: EncoderConfig | null = null;
let encoderFailed = false;
let encoderErrorSeen = false;
let framesWithoutOutput = 0;
let nextFrameIsKeyFrame = false;
let resizeCanvas: OffscreenCanvas | null = null;
let resizeCtx: OffscreenCanvasRenderingContext2D | null = null;
let downscaler: WebGpuDownscaler | null = null;
let senderRotationDeg = 0;
let startTimestamp: number | undefined = undefined;
let lastLoggedFormat: string | null = '(unset)';
let loggedI420Error = false;

// Backpressure
let backpressureDrops = 0;
let backpressureTotalFrames = 0;
let lastBackpressureCheckTime = 0;
const backpressureWindowMs = 5000;
const backpressureDropThreshold = 0.20;
let backpressureNotified = false;

// Segmentation
let onnxSession: ort.InferenceSession | null = null;
let segConfig: SegmentationConfig | null = null;
let resolvedModelConfig: ModelConfig | null = null;
let outputGpuBuffer: GPUBuffer = null!;
let outputTensor: ort.Tensor = null!;
let smoothedMaskBuffer: GPUBuffer = null!;
let blurEnabled = false;
let segInitialized = false;

let segProcessedFrames = 0;
let segTotalInferenceTime = 0;
let segTotalBlurTime = 0;
let segTotalProcessingTime = 0;
let segDroppedFrames = 0;
let segFrameCounter = 0;
let hasValidMask = false;
let loggedBlurFormat = false;

interface QueuedFrame { frame: VideoFrame; sequenceNumber: number; timestamp: number }
let frameQueue: Denque<QueuedFrame> | null = null;
let processingFrame = false;
let frameSequence = 0;

// Streaming
const streamCtx: StreamingContext = {
    sessionToken: '',
    chatId: '',
    serverClockOffsetMs: 0,
    streamKind: 0,
    processing: false,
    apiUrl: null,
    rpcStreamServer: null,
};
let videoStream: InternalVideoStream | null = null;
let lastVideoStream: InternalVideoStream | null = null;

/**
 * Apply a `VideoProcessingConfig.streaming` block to the shared `streamCtx`.
 * The Fusion RPC peer is constructed lazily on the first frame by
 * `InternalVideoStream` / `ensureRpcPush(ctx)`, so this is a pure state copy.
 */
function applyStreamingConfig(config: VideoProcessingConfig): void {
    const s = config.streaming;
    streamCtx.sessionToken = s.sessionToken;
    streamCtx.chatId = s.chatId;
    streamCtx.serverClockOffsetMs = s.serverClockOffsetMs;
    streamCtx.streamKind = s.streamKind ?? 0;
    streamCtx.apiUrl = s.apiUrl;
    streamingEnabled = true;
    // Declare our intent to keep a connection up while we capture.
    // Actual attempts are still gated by `Api.isDotNetRpcConnected`.
    Api.requireConnection('VideoCapture');

    if (!streamCtx.apiUrl)
        warnLog?.log('streaming enabled but apiUrl is empty — push will fail at stream creation');
}
let pendingStreamFrames: VideoStreamFrame[] = [];
let codecSettings: string | null = null;
let storedDescriptionBytes: Uint8Array | null = null;
let firstEncodedTimestamp: number | null = null;
let streamingEnabled = false;

// Pipeline
let processing = false;
let dimensionsReconciled = false;
let needsRotation = false;
let orientationStats: OrientationStats | null = null;
let vadSpeaking = true;
let vadRemoteStreamCount = 0;
let vadReducedFrameIntervalMs = 1000 / 5;
let vadLastPassedFrameTime = 0;
let streamReadLoopPromise: Promise<void> | null = null;

// ─── Segmentation ───────────────────────────────────────────────────────────

function initializeQueue(cfg: SegmentationConfig): void {
    frameQueue = new Denque<QueuedFrame>();
    infoLog?.log(`Segmentation frame queue initialized, maxSize=${cfg.maxQueueSize}`);
}

function enqueueFrame(frame: VideoFrame): void {
    if (!frameQueue || !segConfig) {
        void encodeProcessedFrame(frame);
        return;
    }

    const queuedFrame: QueuedFrame = {
        frame,
        sequenceNumber: frameSequence++,
        timestamp: performance.now(),
    };

    while (frameQueue.length >= segConfig.maxQueueSize) {
        const dropped = frameQueue.shift();
        if (dropped) {
            debugLog?.log(`Dropping frame #${dropped.sequenceNumber} (queue full)`);
            dropped.frame.close();
            segDroppedFrames++;
        }
    }

    frameQueue.push(queuedFrame);
    if (!processingFrame) void processQueue();
}

function emitPreviewAndEncode(frame: VideoFrame): void {
    if (encoder) {
        // Streaming mode: clone for preview, original goes to encoder
        try {
            const previewFrame = new VideoFrame(frame, { timestamp: frame.timestamp });
            void callbacks.onPreviewFrame(previewFrame, rpcNoWait);
        } catch { /* clone failed, skip preview */ }
        void encodeProcessedFrame(frame);
    } else {
        // Preview-only mode: send frame directly as preview, no encoding
        void callbacks.onPreviewFrame(frame, rpcNoWait);
    }
}

async function processQueue(): Promise<void> {
    if (!frameQueue || !segConfig || processingFrame) return;
    processingFrame = true;

    try {
        while (!frameQueue.isEmpty() && processing) {
            const qf = frameQueue.shift();
            if (!qf) break;
            segFrameCounter++;

            processDeferredCleanups();
            processBlurDeferredCleanups();

            const frameSkipInterval = segConfig.frameSkipInterval ?? 1;
            if (frameSkipInterval > 1 && segFrameCounter % frameSkipInterval !== 0 && hasValidMask) {
                const skipBlurStart = performance.now();
                if (segConfig.blurEnabled) {
                    try {
                        await submitBlurI420(
                            qf.frame, smoothedMaskBuffer,
                            segConfig.inputWidth, segConfig.inputHeight,
                            { blurStrength: segConfig.blurRadius, maskDirty: false,
                                outputWidth: segConfig.outputWidth, outputHeight: segConfig.outputHeight },
                            (result) => {
                                if (!loggedBlurFormat) { loggedBlurFormat = true; warnLog?.log(`I420 path: GPU compute shader, frame format: ${result.frame.format}`); }
                                segProcessedFrames++;
                                emitPreviewAndEncode(result.frame);
                            }
                        );
                    } catch {
                        const finalFrame = applyBackgroundBlur(
                            qf.frame, smoothedMaskBuffer, segConfig.inputWidth, segConfig.inputHeight,
                            { blurStrength: segConfig.blurRadius, maskDirty: false,
                                outputWidth: segConfig.outputWidth, outputHeight: segConfig.outputHeight }
                        );
                        segProcessedFrames++;
                        emitPreviewAndEncode(finalFrame);
                    }
                } else {
                    segProcessedFrames++;
                    void encodeProcessedFrame(qf.frame);
                }
                segTotalBlurTime += performance.now() - skipBlurStart;
                segTotalProcessingTime += performance.now() - qf.timestamp;
                continue;
            }

            // Full inference
            const inferenceStartTime = performance.now();

            let inputTensor: ort.Tensor;
            if (resolvedModelConfig!.tensorFormat === 'nchw_float32') {
                inputTensor = await videoFrameToTensorFloat32(qf.frame, segConfig.inputWidth, segConfig.inputHeight);
            } else {
                inputTensor = await videoFrameToTensorUint8(qf.frame, segConfig.inputWidth, segConfig.inputHeight);
            }

            const outputName = onnxSession!.outputNames[0];
            await onnxSession!.run({ [onnxSession!.inputNames[0]]: inputTensor }, { [outputName]: outputTensor });

            const gpuBuffer = outputTensor.gpuBuffer;
            hasValidMask = true;
            returnPooledBuffer(inputTensor.gpuBuffer);

            const inferenceTime = performance.now() - inferenceStartTime;
            const blurStartTime = performance.now();

            if (segConfig.blurEnabled) {
                const smoothingAlpha = segConfig.temporalSmoothingFactor ?? 0.3;
                try {
                    await submitBlurI420(
                        qf.frame, smoothedMaskBuffer, segConfig.inputWidth, segConfig.inputHeight,
                        { blurStrength: segConfig.blurRadius, smoothingSource: gpuBuffer, smoothingAlpha,
                            outputWidth: segConfig.outputWidth, outputHeight: segConfig.outputHeight },
                        (result) => {
                            if (!loggedBlurFormat) { loggedBlurFormat = true; warnLog?.log(`I420 path: GPU compute shader, frame format: ${result.frame.format}`); }
                            segProcessedFrames++;
                            emitPreviewAndEncode(result.frame);
                        }
                    );
                } catch {
                    const finalFrame = applyBackgroundBlur(
                        qf.frame, smoothedMaskBuffer, segConfig.inputWidth, segConfig.inputHeight,
                        { blurStrength: segConfig.blurRadius, smoothingSource: gpuBuffer, smoothingAlpha,
                            outputWidth: segConfig.outputWidth, outputHeight: segConfig.outputHeight }
                    );
                    segProcessedFrames++;
                    emitPreviewAndEncode(finalFrame);
                }
            } else {
                const smoothingAlpha = segConfig.temporalSmoothingFactor ?? 0.3;
                const maskSize = segConfig.inputWidth * segConfig.inputHeight;
                applyTemporalSmoothing(gpuBuffer, smoothedMaskBuffer, maskSize, smoothingAlpha);
                segProcessedFrames++;
                void encodeProcessedFrame(qf.frame);
            }

            segTotalInferenceTime += inferenceTime;
            segTotalBlurTime += performance.now() - blurStartTime;
            segTotalProcessingTime += performance.now() - qf.timestamp;
        }
    } finally {
        processingFrame = false;
    }
}

async function initializeSegmentation(config: SegmentationConfig): Promise<void> {
    segConfig = config;
    ort.env.wasm.wasmPaths = 'https://cdn.jsdelivr.net/npm/onnxruntime-web@1.23.2/dist/';
    ort.env.wasm.numThreads = 1;

    const modelUrl = SegmentationModelUrl;
    const sessionOptions: ort.InferenceSession.SessionOptions = {
        executionProviders: [{ name: 'webgpu', preferredLayout: 'NCHW' }],
        graphOptimizationLevel: 'all', executionMode: 'parallel',
        enableCpuMemArena: true, enableMemPattern: true,
        preferredOutputLocation: 'gpu-buffer', enableGraphCapture: true,
    };

    infoLog?.log('Loading segmentation model from:', modelUrl);
    onnxSession = await ort.InferenceSession.create(modelUrl, sessionOptions);
    infoLog?.log('Segmentation model loaded with WebGPU backend');

    const blurDevice = await ort.env.webgpu.device;
    await WebGPUManager.init(blurDevice);
    await initTensorWebGPU(blurDevice);
    await initBlurWebGPU(blurDevice);
    infoLog?.log('WebGPU resources initialized');

    const maskSize = config.inputWidth * config.inputHeight;
    const gpuDevice = WebGPUManager.get();
    const usage = GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST;
    outputGpuBuffer = gpuDevice.createBuffer({ size: maskSize * 4, usage });
    smoothedMaskBuffer = gpuDevice.createBuffer({ size: maskSize * 4, usage });

    outputTensor = ort.Tensor.fromGpuBuffer(outputGpuBuffer, {
        dataType: 'float32', dims: [1, 1, config.inputHeight, config.inputWidth],
        dispose: () => { /* managed by worker */ },
    });

    resolvedModelConfig = config.modelConfig ?? getModelConfig(modelUrl);
    infoLog?.log(`Model config: format=${resolvedModelConfig.tensorFormat}`);

    initializeQueue(config);
    segInitialized = true;
}

// ─── Encoding pipeline ──────────────────────────────────────────────────────

function processOneFrame(frame: VideoFrame): void {
    if (!processing) { frame.close(); return; }

    // Preview-only mode (no encoder): route through segmentation only
    if (!encoder) {
        if (blurEnabled && segInitialized) {
            enqueueFrame(frame);
        } else {
            // No blur, no encoder — just send as preview and close
            void callbacks.onPreviewFrame(frame, rpcNoWait);
        }
        return;
    }

    if (!encoderConfig) { frame.close(); return; }

    const backpressureMaxQueue = DeviceInfo.isIos ? 1 : 3;
    backpressureTotalFrames++;
    if (encoder.getEncodeQueueSize() > backpressureMaxQueue) {
        frame.close();
        backpressureDrops++;
        const now = performance.now();
        if (now - lastBackpressureCheckTime > backpressureWindowMs) {
            const dropRate = backpressureDrops / backpressureTotalFrames;
            if (dropRate > backpressureDropThreshold && !backpressureNotified) {
                backpressureNotified = true;
                warnLog?.log(`Sustained backpressure: dropRate=${(dropRate * 100).toFixed(1)}%`);
                void callbacks.onBackpressure(dropRate, rpcNoWait);
            }
            backpressureDrops = 0; backpressureTotalFrames = 0; lastBackpressureCheckTime = now;
        }
        return;
    }

    if (backpressureNotified && backpressureTotalFrames > 30) {
        const now = performance.now();
        if (now - lastBackpressureCheckTime > backpressureWindowMs) {
            if (backpressureDrops / backpressureTotalFrames < 0.05) backpressureNotified = false;
            backpressureDrops = 0; backpressureTotalFrames = 0; lastBackpressureCheckTime = now;
        }
    }

    if (blurEnabled && segInitialized) {
        enqueueFrame(frame);
    } else {
        void encodeProcessedFrame(frame);
    }
}

async function encodeProcessedFrame(frame: VideoFrame): Promise<void> {
    if (!encoder || !processing || !encoderConfig) { frame.close(); return; }

    try {
        if (startTimestamp === undefined) {
            startTimestamp = frame.timestamp;
            infoLog?.log(`Start timestamp set to ${startTimestamp}μs`);
        }

        let processedFrame: VideoFrame;
        if (downscaler) {
            // WebGPU path: keeps frame on GPU. Uses VideoFrame.rotation when set,
            // else senderRotationDeg (main-thread supplies from screen.orientation).
            const results = downscaler.process(frame, senderRotationDeg);
            processedFrame = results[0].frame;
        } else {
            const resized = resizeFrame(frame, encoderConfig.width, encoderConfig.height, resizeCanvas, resizeCtx, needsRotation);
            processedFrame = resized.frame;
            resizeCanvas = resized.canvas;
            resizeCtx = resized.ctx;
        }

        // Optional YUV pre-conversion — disabled by default since HW encoders accept RGBA natively.
        // Enable via EncoderConfig.preConvertYuv for devices where pre-conversion helps encoding perf.
        if (encoderConfig.preConvertYuv) {
            const format = processedFrame.format as string | null;
            const isAlreadyYuv = format === 'NV12' || format === 'I420'
                || format === 'I420A' || format === 'I422' || format === 'I444' || format === 'NV12A';

            if (!isAlreadyYuv) {
                let converted = false;
                for (const fmt of ['I420', 'NV12', 'I420A', 'I422'] as const) {
                    try {
                        const size = processedFrame.allocationSize({ format: fmt });
                        const buf = new ArrayBuffer(size);
                        const layout = await processedFrame.copyTo(buf, { format: fmt });
                        const yuvFrame = new VideoFrame(buf, {
                            format: fmt, codedWidth: processedFrame.codedWidth, codedHeight: processedFrame.codedHeight,
                            timestamp: processedFrame.timestamp, duration: processedFrame.duration ?? undefined,
                            layout, colorSpace: processedFrame.colorSpace,
                        });
                        processedFrame.close();
                        processedFrame = yuvFrame;
                        converted = true;
                        break;
                    } catch { continue; }
                }

                if (!converted) {
                    try {
                        const result = cpuRgbaToI420(processedFrame, resizeCanvas, resizeCtx);
                        processedFrame = result.frame;
                        resizeCanvas = result.canvas;
                        resizeCtx = result.ctx;
                    } catch (e) {
                        if (!loggedI420Error) { loggedI420Error = true; warnLog?.log('All YUV conversion methods failed:', String(e)); }
                    }
                }
            }
        }

        if (processedFrame.format !== lastLoggedFormat) {
            lastLoggedFormat = processedFrame.format;
            warnLog?.log(`Frame format: ${processedFrame.format}, ${processedFrame.codedWidth}x${processedFrame.codedHeight}`);
        }

        // Timestamp normalization
        const normalizedTs = processedFrame.timestamp - startTimestamp;
        if (normalizedTs !== processedFrame.timestamp) {
            const normalized = new VideoFrame(processedFrame, { timestamp: normalizedTs, duration: processedFrame.duration ?? undefined });
            processedFrame.close();
            processedFrame = normalized;
        }

        // Let the encoder decide keyframes based on its keyframeInterval config
        // (set by recording-service.ts: ~2s screencast, ~3s webcam)
        const forceKf = nextFrameIsKeyFrame;
        nextFrameIsKeyFrame = false;
        encoder.encode(processedFrame, forceKf);
        framesWithoutOutput++;

        // Detect dead encoder: error seen + 90 frames (3s@30fps) with zero output
        if (encoderErrorSeen && framesWithoutOutput > 90 && !encoderFailed) {
            encoderFailed = true;
            const codec = encoderConfig.codec;
            errorLog?.log(`Encoder dead: ${codec} — ${framesWithoutOutput} frames with no output after error`);
            void callbacks.onEncoderFailed(codec, rpcNoWait);
        }
    } catch (error) {
        errorLog?.log('Error encoding frame:', error);
        try { frame.close(); } catch { /* already closed */ }
    }
}

function onEncoderOutput(chunkData: EncodedChunkData): void {
    framesWithoutOutput = 0;
    encoderErrorSeen = false;

    const chunkBuffer = new ArrayBuffer(chunkData.byteLength);
    chunkData.chunk.copyTo(new Uint8Array(chunkBuffer));

    let actualCodec = encoderConfig!.codec;
    let descBuffer: ArrayBuffer | undefined;

    if (chunkData.type === 'key' && chunkData.metadata?.decoderConfig?.description) {
        const desc = chunkData.metadata.decoderConfig.description;
        let sourceArray: Uint8Array;
        if (desc instanceof ArrayBuffer) sourceArray = new Uint8Array(desc);
        else if (desc instanceof SharedArrayBuffer) sourceArray = new Uint8Array(desc);
        else if (ArrayBuffer.isView(desc)) sourceArray = new Uint8Array(desc.buffer, desc.byteOffset, desc.byteLength);
        else sourceArray = new Uint8Array(desc as ArrayBuffer);

        descBuffer = new ArrayBuffer(sourceArray.byteLength);
        new Uint8Array(descBuffer).set(sourceArray);

        if (isAvcCDescription(descBuffer) && !encoderConfig!.codec.startsWith('avc1')) {
            const derivedCodec = deriveAvcCodecFromDescription(descBuffer);
            warnLog?.log(`Encoder output mismatch: configured=${encoderConfig!.codec} but output is avcC, correcting to ${derivedCodec}`);
            actualCodec = derivedCodec;
            encoderConfig!.codec = derivedCodec;
        }
    }

    if (streamingEnabled) {
        deliverChunkToStream(chunkBuffer, chunkData.chunk.timestamp, chunkData.chunk.duration ?? 0,
            chunkData.type === 'key', actualCodec, chunkData.sequenceNumber, descBuffer, chunkData.temporalLayerId);
    } else {
        void callbacks.onSerializedChunk(
            chunkBuffer, chunkData.chunk.timestamp, chunkData.chunk.duration ?? 0,
            chunkData.type === 'key', actualCodec, chunkData.sequenceNumber, descBuffer, rpcNoWait);
    }
}

function deliverChunkToStream(
    chunkBytes: ArrayBuffer,
    timestamp: number,
    duration: number,
    isKeyFrame: boolean,
    codec: string,
    sequenceNumber: number,
    descriptionBytes?: ArrayBuffer,
    temporalLayerId?: number
): void {
    // Detect sender disconnect BEFORE normalizing timestamps.
    //
    // With Fusion 12.3.25 + allowReconnect=true on the PushVideo RpcStream, same-peer
    // WS reconnects no longer dispose the sender — Fusion's $sys.Reconnect + real-time
    // resume (skip-to-next-keyframe via canSkipTo) handle continuity transparently.
    //
    // isDisposed therefore only fires on peer-change (sharedObjects.disconnectAll →
    // AbortSignal → source generator exits → stream.whenSent resolves → finally
    // { isDisposed = true }) or after the server-side MaxLiveDuration CTS. In both
    // cases the next PushVideo call targets a new server instance / new chat entry,
    // so the first frame must start a fresh timing anchor at offset 0 — otherwise
    // the viewer sees offsetMs≈past-stream-length and stalls.
    if (videoStream?.isDisposed) {
        warnLog?.log('VideoStream disposed (peer-change) — will recreate on next keyframe');
        videoStream = null;
        firstEncodedTimestamp = null;
    }

    const chunkData = new Uint8Array(chunkBytes);
    firstEncodedTimestamp ??= timestamp;
    const normalizedTimestamp = timestamp - firstEncodedTimestamp;

    const frame: VideoStreamFrame = {
        offset: microsecondsToTicks(normalizedTimestamp),
        duration: microsecondsToTicks(duration),
        isKeyFrame,
        width: encoderConfig!.width, height: encoderConfig!.height,
        data: chunkData, codec: isKeyFrame ? codec : undefined,
        temporalLayerId: temporalLayerId,
    };

    if (isKeyFrame) {
        debugLog?.log(`Streaming keyframe: seq=${sequenceNumber}, offsetMs=${(normalizedTimestamp / 1000).toFixed(0)}, ${(chunkData.length / 1024).toFixed(2)} KB`);
    }

    if (isKeyFrame && descriptionBytes && descriptionBytes.byteLength > 0) {
        const descBytes = new Uint8Array(descriptionBytes);

        frame.description = descBytes;
        if (!codecSettings) {
            let binary = '';
            for (const byte of descBytes) binary += String.fromCharCode(byte);
            codecSettings = btoa(binary);
            debugLog?.log('Captured codec description:', descBytes.length, 'bytes');
        }
        storedDescriptionBytes = descBytes;
    }

    if (isKeyFrame && !frame.description && storedDescriptionBytes) {
        frame.description = storedDescriptionBytes;
    }

    if (!videoStream) {
        const isAV1 = encoderConfig!.codec.startsWith('av01');
        const canCreateStream = codecSettings ?? (isAV1 && isKeyFrame);
        if (canCreateStream) {
            const settings = codecSettings ?? '';
            infoLog?.log(`Creating VideoStream: codec=${encoderConfig!.codec}, ${encoderConfig!.width}x${encoderConfig!.height}, codecSettings=${settings.length} chars`);
            videoStream = new InternalVideoStream(
                { codec: encoderConfig!.codec, width: encoderConfig!.width, height: encoderConfig!.height, codecSettings: settings },
                streamCtx,
                lastVideoStream?.whenDisposed,
            );
            lastVideoStream = videoStream;
            warnLog?.log(`TIMING_ANCHOR: firstEncodedTimestamp=${(firstEncodedTimestamp / 1000).toFixed(0)}ms`);
            for (const buffered of pendingStreamFrames) videoStream.addFrame(buffered);
            pendingStreamFrames = [];
            videoStream.addFrame(frame);
            void callbacks.onStreamCreated(settings, rpcNoWait);
        } else {
            pendingStreamFrames.push(frame);
        }
    } else {
        videoStream.addFrame(frame);
    }
}

function createEncoder(config: EncoderConfig): void {
    encoderConfig = config;
    encoder = new WebCodecsEncoder(config, onEncoderOutput, (error) => {
        errorLog?.log('Encoder error:', error.name, error.message);
        encoderErrorSeen = true;
    });
    encoder.initialize();
}

// Init WebGPU downscaler for the current encoder dims. Returns null if WebGPU
// is unavailable — caller then relies on the legacy canvas resizeFrame path.
async function initDownscaler(width: number, height: number): Promise<void> {
    if (downscaler) {
        downscaler.configure([{ width, height }]);
        return;
    }
    try {
        const device = await WebGPUManager.init();
        downscaler = new WebGpuDownscaler(device);
        downscaler.configure([{ width, height }]);
        infoLog?.log(`Downscaler initialized: ${width}x${height}`);
    } catch (e) {
        warnLog?.log('WebGPU downscaler unavailable, falling back to canvas resize:', e);
        downscaler = null;
    }
}

// ─── Stream read loop ───────────────────────────────────────────────────────

async function streamReadLoop(inputReader: ReadableStreamDefaultReader<VideoFrame>): Promise<void> {
    try {
        while (processing) {
            const { done, value: rawFrame } = await inputReader.read();
            if (done) { infoLog?.log('Stream input ended'); break; }

            if (!encoder || !processing || !encoderConfig) { // eslint-disable-line @typescript-eslint/no-unnecessary-condition
                rawFrame.close(); continue;
            }

            const frameRotation = rawFrame.rotation ?? null;

            if (!dimensionsReconciled) {
                dimensionsReconciled = true;
                const frameW = rawFrame.displayWidth;
                const frameH = rawFrame.displayHeight;
                const codedW = rawFrame.codedWidth;
                const codedH = rawFrame.codedHeight;
                warnLog?.log(`DIMENSIONS: display=${frameW}x${frameH}, coded=${codedW}x${codedH}, config=${encoderConfig.width}x${encoderConfig.height}, rotation=${frameRotation ?? 'N/A'}`);
                // Detect rotation: display dims are transposed vs encoder config
                // (MSTP gives raw sensor dims as displayWidth/Height)
                const isRotated = frameW === encoderConfig.height && frameH === encoderConfig.width
                    && frameW !== encoderConfig.width;
                // Detect rotation: Safari sets display dims to post-rotation (portrait) but pixel
                // buffer stays in sensor orientation (landscape) — coded dims reveal true pixel layout
                const isRotatedByCoded = !isRotated
                    && codedW === frameH && codedH === frameW && codedW !== frameW;
                let detection: OrientationStats['rotationDetection'] = 'none';
                if (isRotated || isRotatedByCoded) {
                    warnLog?.log(`Frame ${frameW}x${frameH} (coded: ${codedW}x${codedH}) is rotated vs config ${encoderConfig.width}x${encoderConfig.height}`);
                    needsRotation = true;
                    detection = isRotated ? 'dimensions' : 'coded';
                    // Keep encoder config at portrait dimensions — resizeFrame() rotate90 will
                    // rotate landscape frames into portrait before encoding
                } else if (frameW !== encoderConfig.width || frameH !== encoderConfig.height) {
                    const inputPixels = frameW * frameH;
                    const configPixels = encoderConfig.width * encoderConfig.height;
                    if (inputPixels <= configPixels) {
                        // Input is smaller than config — reconfigure encoder to match
                        // (avoids upscaling which wastes CPU for no quality gain)
                        warnLog?.log(`Display dimensions ${frameW}x${frameH} smaller than config ${encoderConfig.width}x${encoderConfig.height}, reconfiguring`);
                        encoderConfig.width = frameW; encoderConfig.height = frameH;
                        await encoder.reconfigure({ width: frameW, height: frameH, bitrate: encoderConfig.bitrate });
                        if (segConfig) { segConfig.outputWidth = frameW; segConfig.outputHeight = frameH; }
                        void callbacks.onDimensionReconciled(frameW, frameH, rpcNoWait);
                    } else {
                        // Input is larger than config (e.g., 4K screen capture with 1080p encoder config).
                        // Keep encoder at configured resolution — resizeFrame() will downscale.
                        debugLog?.log(`Display dimensions ${frameW}x${frameH} larger than config ${encoderConfig.width}x${encoderConfig.height}, resize canvas will downscale`);
                    }
                }
                // Record rotation metadata even when no other detector fires — useful for diagnostics
                if (detection === 'none' && frameRotation !== null && frameRotation % 180 !== 0)
                    detection = 'metadata';
                orientationStats = {
                    firstDisplayWidth: frameW, firstDisplayHeight: frameH,
                    firstCodedWidth: codedW, firstCodedHeight: codedH,
                    firstRotation: frameRotation, lastRotation: frameRotation,
                    configuredWidth: encoderConfig.width, configuredHeight: encoderConfig.height,
                    needsRotation, rotationDetection: detection, framesSeen: 1,
                };
            } else if (orientationStats) {
                orientationStats.framesSeen++;
                orientationStats.lastRotation = frameRotation;
            }

            // Sensor is physically landscape; VideoFrame.rotation tells us how the
            // frame should be displayed. If display orientation crosses the 90°
            // boundary (portrait ↔ landscape), swap encoder dims and reconfigure
            // downscaler so output aspect matches user's device orientation.
            // Fall back to senderRotationDeg when frame.rotation is null (Safari
            // iOS MSTP): main thread derives it from screen.orientation.
            const rotDeg = frameRotation ?? senderRotationDeg;
            const displayPortrait = rotDeg === 90 || rotDeg === 270;
            const encoderPortrait = encoderConfig.height > encoderConfig.width;
            if (displayPortrait !== encoderPortrait) {
                const newW = encoderConfig.height;
                const newH = encoderConfig.width;
                try {
                    await encoder.reconfigure({ width: newW, height: newH, bitrate: encoderConfig.bitrate });
                    encoderConfig.width = newW;
                    encoderConfig.height = newH;
                    if (downscaler) downscaler.configure([{ width: newW, height: newH }]);
                    if (segConfig) { segConfig.outputWidth = newW; segConfig.outputHeight = newH; }
                    void callbacks.onDimensionReconciled(newW, newH, rpcNoWait);
                    infoLog?.log(`Orientation change: rotation=${rotDeg} → encoder ${newW}x${newH}`);
                } catch (e) {
                    warnLog?.log('Orientation reconfigure failed:', e);
                }
            }

            const frame = rawFrame;

            if (!vadSpeaking && vadRemoteStreamCount >= 2) {
                const now = performance.now();
                if (now - vadLastPassedFrameTime < vadReducedFrameIntervalMs) { frame.close(); continue; }
                vadLastPassedFrameTime = now;
            }

            processOneFrame(frame);
        }
    } catch (error) {
        if (processing) errorLog?.log('Stream read error:', error);
    } finally {
        try { inputReader.releaseLock(); } catch { /* ignore */ }
    }
}

// ─── Server implementation ──────────────────────────────────────────────────

export const serverImpl: VideoProcessingWorker = {

    startWithStream: async (config, frameInputStream): Promise<void> => {
        try {
            infoLog?.log('Starting video processing worker (stream mode)...');
            applyStreamingConfig(config);
            senderRotationDeg = config.senderRotationDeg ?? 0;

            if (config.segmentation) {
                await initializeSegmentation({ ...config.segmentation, outputWidth: config.encoder.width, outputHeight: config.encoder.height });
                blurEnabled = true;
                infoLog?.log('Segmentation initialized (blur enabled)');
            }

            createEncoder(config.encoder);
            await initDownscaler(config.encoder.width, config.encoder.height);

            if (config.adaptiveFramerate) {
                vadReducedFrameIntervalMs = 1000 / config.adaptiveFramerate.reducedFps;
            }

            processing = true;
            streamCtx.processing = true;
            dimensionsReconciled = false;
            needsRotation = false;
            orientationStats = null;

            const inputReader = frameInputStream.getReader();
            streamReadLoopPromise = streamReadLoop(inputReader);

            infoLog?.log('Video processing worker started (stream mode)');
        } catch (error) {
            errorLog?.log('Failed to start stream mode:', error);
            throw error;
        }
    },

    startWithTrack: async (config, track): Promise<void> => {
        try {
            infoLog?.log('Starting video processing worker (track transfer mode)...');

            // Check if MSTP is available in worker scope
            if (typeof MediaStreamTrackProcessor === 'undefined') {
                throw new Error('MediaStreamTrackProcessor not available in worker scope');
            }

            applyStreamingConfig(config);
            senderRotationDeg = config.senderRotationDeg ?? 0;

            if (config.segmentation) {
                await initializeSegmentation({ ...config.segmentation, outputWidth: config.encoder.width, outputHeight: config.encoder.height });
                blurEnabled = true;
                infoLog?.log('Segmentation initialized (blur enabled)');
            }

            createEncoder(config.encoder);
            await initDownscaler(config.encoder.width, config.encoder.height);

            if (config.adaptiveFramerate) {
                vadReducedFrameIntervalMs = 1000 / config.adaptiveFramerate.reducedFps;
            }

            processing = true;
            streamCtx.processing = true;
            dimensionsReconciled = false;
            needsRotation = false;
            orientationStats = null;

            const processor = new MediaStreamTrackProcessor({ track });
            const inputReader = processor.readable.getReader();
            streamReadLoopPromise = streamReadLoop(inputReader);

            infoLog?.log('Video processing worker started (track transfer mode)');
        } catch (error) {
            errorLog?.log('Failed to start track transfer mode:', error);
            throw error;
        }
    },

    initialize: async (config): Promise<void> => {
        try {
            const isPreviewOnly = config.previewOnly === true;
            infoLog?.log(`Starting video processing worker (${isPreviewOnly ? 'preview-only' : 'RPC'} mode)...`);

            if (!isPreviewOnly) {
                applyStreamingConfig(config);
            }
            senderRotationDeg = config.senderRotationDeg ?? 0;

            if (config.segmentation) {
                await initializeSegmentation({ ...config.segmentation, outputWidth: config.encoder.width, outputHeight: config.encoder.height });
                blurEnabled = true;
            }

            if (!isPreviewOnly) {
                createEncoder(config.encoder);
                await initDownscaler(config.encoder.width, config.encoder.height);
            }

            if (config.adaptiveFramerate) {
                vadReducedFrameIntervalMs = 1000 / config.adaptiveFramerate.reducedFps;
            }

            processing = true;
            streamCtx.processing = true;

            infoLog?.log(`Video processing worker started (${isPreviewOnly ? 'preview-only' : 'RPC'} mode)`);
        } catch (error) {
            errorLog?.log('Failed to initialize:', error);
            throw error;
        }
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    encodeFrame: async (frame): Promise<void> => { processOneFrame(frame); },

    // eslint-disable-next-line @typescript-eslint/require-await
    setVadState: async (speaking, remoteStreamCount): Promise<void> => {
        vadSpeaking = speaking;
        vadRemoteStreamCount = remoteStreamCount;
        if (speaking) vadLastPassedFrameTime = 0;
        debugLog?.log(`VAD state: speaking=${speaking}, remoteStreamCount=${remoteStreamCount}`);
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    setSenderRotation: async (rotationDeg): Promise<void> => {
        senderRotationDeg = ((rotationDeg % 360) + 360) % 360;
        infoLog?.log(`Sender rotation set to ${senderRotationDeg}°`);
    },

    reconfigure: async (params): Promise<void> => {
        if (!encoder || !processing || !encoderConfig) { warnLog?.log('Cannot reconfigure: not active'); return; }
        // Preserve encoder orientation: map incoming dimensions by magnitude
        const inSmall = Math.min(params.width, params.height);
        const inLarge = Math.max(params.width, params.height);
        const isPortrait = encoderConfig.height > encoderConfig.width;
        params.width = isPortrait ? inSmall : inLarge;
        params.height = isPortrait ? inLarge : inSmall;
        infoLog?.log(`Reconfigure: ${params.bitrate / 1_000_000}Mbps, ${params.width}x${params.height}`);
        encoderConfig.bitrate = params.bitrate; encoderConfig.width = params.width; encoderConfig.height = params.height;
        await encoder.reconfigure(params);
        resizeCanvas = null; resizeCtx = null;
        if (downscaler) downscaler.configure([{ width: params.width, height: params.height }]);
        if (segConfig && blurEnabled) { segConfig.outputWidth = params.width; segConfig.outputHeight = params.height; }
    },

    switchCodec: async (config: EncoderConfig): Promise<void> => {
        if (!encoder) { warnLog?.log('Cannot switch codec: not active'); return; }
        infoLog?.log(`Switching codec to ${config.codec}`);

        // Suppress frame output during codec switch — encoder.switchCodec() flushes
        // old-codec frames that must NOT leak into the new stream
        const wasStreaming = streamingEnabled;
        streamingEnabled = false;

        if (videoStream) {
            videoStream.complete();
            try { await videoStream.whenDisposed; } catch { /* ignore */ }
            videoStream = null;
        }
        codecSettings = null; firstEncodedTimestamp = null; pendingStreamFrames = []; storedDescriptionBytes = null;
        await encoder.switchCodec(config);

        // Re-enable streaming and clear any frames that leaked during flush
        streamingEnabled = wasStreaming;
        pendingStreamFrames = [];
        encoderConfig = config; resizeCanvas = null; resizeCtx = null;
        if (downscaler) downscaler.configure([{ width: config.width, height: config.height }]);
        startTimestamp = undefined;
        backpressureDrops = 0; backpressureTotalFrames = 0; lastBackpressureCheckTime = 0; backpressureNotified = false;
        encoderFailed = false; encoderErrorSeen = false; framesWithoutOutput = 0;
        infoLog?.log('Codec switched successfully');
    },

    toggleBlur: async (enabled, segCfg?): Promise<void> => {
        infoLog?.log(`Toggling blur: ${enabled ? 'ON' : 'OFF'}`);
        if (enabled && !segInitialized) {
            if (!segCfg && !segConfig) throw new Error('Cannot enable blur: no segmentation config');
            const cfg = segCfg ?? segConfig!;
            await initializeSegmentation({
                ...cfg,
                outputWidth: encoderConfig?.width ?? cfg.outputWidth,
                outputHeight: encoderConfig?.height ?? cfg.outputHeight,
            });
        }
        if (segConfig) segConfig.blurEnabled = enabled;
        blurEnabled = enabled;
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    forceKeyFrame: async (): Promise<void> => { nextFrameIsKeyFrame = true; infoLog?.log('Forced next frame to be keyframe'); },

    flush: async (): Promise<void> => {
        if (encoder) { try { await encoder.flush(); infoLog?.log('Encoder flushed'); } catch (e) { warnLog?.log('Encoder flush error:', e); } }
    },

    stop: async (): Promise<void> => {
        infoLog?.log('Stopping video processing worker...');
        processing = false;
        streamCtx.processing = false;

        if (streamReadLoopPromise) { try { await streamReadLoopPromise; } catch { /* ignore */ } streamReadLoopPromise = null; }
        try { await awaitAllPendingReadbacks(); } catch { /* ignore */ }
        if (frameQueue) { while (!frameQueue.isEmpty()) { const qf = frameQueue.shift(); if (qf) qf.frame.close(); } frameQueue = null; }
        if (encoder) { try { await encoder.flush(); encoder.close(); } catch (e) { warnLog?.log('Encoder close error:', e); } }
        if (videoStream) {
            videoStream.complete();
            // Wait for stream loop to finish sending remaining frames before closing connection
            try { await videoStream.whenDisposed; } catch { /* ignore */ }
            videoStream = null;
        }
        // Also await lastVideoStream if it's a different instance (e.g. codec switch created
        // a new stream while the old one was still draining). Without this, closing the peer
        // kills the connection before $sys.End is sent → "Connection is closed prematurely".
        if (lastVideoStream) {
            lastVideoStream.complete();
            try { await lastVideoStream.whenDisposed; } catch { /* ignore */ }
            lastVideoStream = null;
        }
        Api.releaseConnection('VideoCapture');
        streamCtx.rpcStreamServer = null;
        if (segInitialized) { try { outputGpuBuffer.destroy(); } catch { /* ignore */ } try { smoothedMaskBuffer.destroy(); } catch { /* ignore */ } }

        if (downscaler) { try { downscaler.dispose(); } catch { /* ignore */ } downscaler = null; }

        // Reset all state
        encoder = null; encoderConfig = null; onnxSession = null; segConfig = null; resolvedModelConfig = null;
        segInitialized = false; blurEnabled = false; resizeCanvas = null; resizeCtx = null;
        startTimestamp = undefined; lastLoggedFormat = '(unset)'; loggedI420Error = false;
        backpressureDrops = 0; backpressureTotalFrames = 0; lastBackpressureCheckTime = 0; backpressureNotified = false;
        dimensionsReconciled = false; needsRotation = false; orientationStats = null; vadSpeaking = true; vadRemoteStreamCount = 0; vadLastPassedFrameTime = 0;
        segFrameCounter = 0; hasValidMask = false; loggedBlurFormat = false; processingFrame = false; frameSequence = 0;
        segProcessedFrames = 0; segTotalInferenceTime = 0; segTotalBlurTime = 0; segTotalProcessingTime = 0; segDroppedFrames = 0;
        videoStream = null; lastVideoStream = null; pendingStreamFrames = [];
        codecSettings = null; storedDescriptionBytes = null; firstEncodedTimestamp = null; streamingEnabled = false;

        infoLog?.log('Video processing worker stopped');
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    getStats: async (): Promise<VideoProcessingStats> => {
        const encoderStats = encoder?.getStats() ?? {
            encodedFrames: 0, droppedFrames: 0, keyFrames: 0, totalBytes: 0,
            averageEncodeTime: 0, medianEncodeTime: 0, pureMedianEncodeTime: -1,
            configuredWidth: 0, configuredHeight: 0, configuredBitrate: 0, hardwareAcceleration: 'unknown',
        };
        const segStats: SegmentationStats | null = segInitialized ? {
            processedFrames: segProcessedFrames,
            averageInferenceTime: segProcessedFrames > 0 ? segTotalInferenceTime / segProcessedFrames : 0,
            averageBlurTime: segProcessedFrames > 0 ? segTotalBlurTime / segProcessedFrames : 0,
            averageTotalTime: segProcessedFrames > 0 ? segTotalProcessingTime / segProcessedFrames : 0,
            droppedFrames: segDroppedFrames, backend: segConfig?.backend ?? 'unknown',
        } : null;
        return { encoder: encoderStats, segmentation: segStats, orientation: orientationStats ? { ...orientationStats } : null };
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    updateSessionToken: async (token): Promise<void> => { streamCtx.sessionToken = token; },

    // eslint-disable-next-line @typescript-eslint/require-await
    updateServerClockOffset: async (offsetMs): Promise<void> => { streamCtx.serverClockOffsetMs = offsetMs; },

    // eslint-disable-next-line @typescript-eslint/require-await
    onConnectivityUpdate: async (isOnline, isConnected, isBlazorServer): Promise<void> => {
        WorkerConnectivityUI.update(isOnline, isConnected, isBlazorServer);
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    disconnectApi: async (): Promise<void> => {
        // Debug-only path — invoked by DebugUI.disconnectApi(WorkerKind.VideoCapture).
        // Closes the WS connection; the peer's reconnect loop reopens it.
        warnLog?.log(`disconnectApi (debug): disconnecting peer`);
        try {
            if (Api.hub.defaultPeerUrl !== undefined)
                Api.hub.peers.get(Api.hub.defaultPeerUrl)?.disconnect();
        } catch (e) {
            warnLog?.log(`disconnectApi: Api not initialized`, e);
        }
    },
};
