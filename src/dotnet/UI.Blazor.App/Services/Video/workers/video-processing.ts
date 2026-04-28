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
import type { SegmentationConfig, SegmentationStats, ModelConfig, SpatialLayerConfig, VideoProcessingConfig, VideoProcessingWorker, VideoProcessingWorkerCallbacks, VideoProcessingStats, VideoProcessingStreamingStats, OrientationStats } from './video-processing-worker-contract';
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
import { WebGpuDownscaler, type DownscaleTarget } from '../webgpu-downscaler';

import { isAvcCDescription, deriveAvcCodecFromDescription, pickAvcLevelByte, resizeFrame, cpuRgbaToI420 } from './video-encoding-helpers';
import { getCodecCategory } from '../codec-support';
import {
    type VideoStreamFrame, type StreamingContext,
    microsecondsToTicks, InternalVideoStream,
} from './video-streaming';
import { Api } from 'api';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';

// Import the ONNX model so esbuild copies it to dist/assets/onnx/
import SegmentationModelUrl from './selfie_segmentation_olive_webgpu.onnx';

// Type declarations for Insertable Streams API (may be available in worker scope on Safari 18+)
declare class MediaStreamTrackProcessor<T = VideoFrame> {
    constructor(options: { track: MediaStreamTrack });
    readable: ReadableStream<T>;
}
// Structural ctor types for the two output-track flavours: standardized
// `VideoTrackGenerator` (Safari 18+) and Chromium's older proprietary
// `MediaStreamTrackGenerator`. Both produce a writable VideoFrame stream + a
// readable MediaStreamTrack. We probe `self` for these names at runtime.
type VideoTrackGeneratorCtor = new () => { writable: WritableStream<VideoFrame>; track: MediaStreamTrack };
type MediaStreamTrackGeneratorCtor = new (opts: { kind: 'video' }) => { writable: WritableStream<VideoFrame>; track: MediaStreamTrack };

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoPipeline');

// ─── Callbacks (set by worker entry after RPC init) ─────────────────────────

let callbacks: VideoProcessingWorkerCallbacks;

export function setCallbacks(cb: VideoProcessingWorkerCallbacks): void {
    callbacks = cb;
}

// ─── State ──────────────────────────────────────────────────────────────────

// Encoder. `encoder` is the base layer (SpatialLayerId=0) and is always present
// when encoding is active. `extraLayerEncoders` holds simulcast layers 1..N when
// VideoProcessingConfig.spatialLayers is set, each tagged with its SpatialLayerId
// via the WebCodecsEncoder ctor. Empty for single-encoder (P2P) mode.
let encoder: WebCodecsEncoder | null = null;
let extraLayerEncoders: WebCodecsEncoder[] = [];
let encoderConfig: EncoderConfig | null = null;

// Encoder pool — survives across stop/start cycles to keep the NVENC session
// slot held during the gap. Without this, a fresh `VideoEncoder.configure()`
// after stop collides with the previous session's still-releasing handle and
// fails with `OperationError: Encoder initialization error`. Pool is purged
// after POOL_TTL_MS of idle to avoid retaining HW indefinitely.
interface PrimaryPoolEntry {
    encoder: WebCodecsEncoder;
    codec: string;
    width: number;
    height: number;
}
let pooledPrimary: PrimaryPoolEntry | null = null;
let poolExpireTimer: ReturnType<typeof setTimeout> | null = null;
const POOL_TTL_MS = 30_000;
// True once .initialize() (configure) has been called on `encoder` and any
// extras. Encoder.configure() is deferred to the first encoded frame so the
// configure call sees the FINAL dims (after first-frame rotation/dim reconcile)
// and avoids the configure→reconfigure flip-flop that crashes Chrome HW HEVC.
let encodersInitialized = false;
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
let loggedPreConvertSkipped = false;
let loggedPreviewCloneError = false;

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
    streamStatus = 'waiting for first frame';
    // Declare our intent to keep a connection up while we capture.
    // Actual attempts are still gated by `Api.isDotNetRpcConnected`.
    Api.requireConnection('VideoCapture');

    if (!streamCtx.apiUrl)
        warnLog?.log('streaming enabled but apiUrl is empty — push will fail at stream creation');
}
let pendingStreamFrames: VideoStreamFrame[] = [];
let codecSettings: string | null = null;
const storedDescriptionBytesByLayer = new Map<number, Uint8Array>();
let streamingEnabled = false;
let streamRecreations = 0;
let streamStatus = 'idle';
let lastStreamError = '';

// ─── Screencast heartbeat ──────────────────────────────────────────────────
// getDisplayMedia is change-driven: a static screen produces zero frames. Without
// traffic, (a) the server's frame-silence watchdog reaps the stream, and (b) the
// receiver's SKIP_TO_LIVE check (3s threshold) trips and force-reloads, both
// pointlessly on unchanged content. We re-encode the cached last frame every
// SCREENCAST_HEARTBEAT_MS — interval sits well below the 3s receiver threshold
// so receiver latency stays bounded. Keyframes are auto-promoted by the encoder
// via maxKeyFrameIntervalMs=2000 (see recording-service.ts), so we don't force
// one here; encoder emits tiny P-frames for identical content most of the time
// (a few hundred bytes) and a real keyframe every ~2s for mid-stream joiners.
// Webcam skips this — its sensor emits continuously, so the path is dead code
// for streamKind=0.
const SCREENCAST_HEARTBEAT_MS = 1_000;
const SCREENCAST_HEARTBEAT_CHECK_MS = 500;
let lastEncodedFrame: VideoFrame | null = null;
let lastEncodedFrameAt = 0; // performance.now() of the last encoder.encode() we drove
let lastEncodedTimestampUs = 0; // μs timestamp of the last frame fed to encoder (real or heartbeat)
let heartbeatTimer: ReturnType<typeof setInterval> | null = null;

// Pipeline
let processing = false;
let dimensionsReconciled = false;
let needsRotation = false;
let orientationStats: OrientationStats | null = null;
// Native source dimensions as they arrive from MSTP (before any downscale). Sent to
// server on PushVideo so it knows the upscaling headroom — e.g. a screencast encoder
// configured at 1080p can be asked to step up to 4K only if the source is actually 4K.
let sourceWidth = 0;
let sourceHeight = 0;
let vadSpeaking = true;
let vadRemoteStreamCount = 0;
let vadReducedFrameIntervalMs = 1000 / 5;
let vadLastPassedFrameTime = 0;
let streamReadLoopPromise: Promise<void> | null = null;

// Preview-track output (MSTG path). When set, processed frames are written to
// `previewWriter` instead of being copied across the RPC boundary as VideoFrames.
let previewWriter: WritableStreamDefaultWriter<VideoFrame> | null = null;
let previewWriterClosed = false;

// Input track held by `startWithTrack` / `startPreviewWithTrack` (MSTP source).
// Tracked here so `stop()` can explicitly release it instead of relying on GC
// of the MSTP processor — keeps the camera light from lingering.
let inputTrack: MediaStreamTrack | null = null;

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
        // Streaming mode: encoder consumes the frame; preview is emitted from
        // *inside* `encodeProcessedFrame` after rotation+downscale so what the
        // remote peer sees is what the local preview shows (WYSIWYG).
        void encodeProcessedFrame(frame);
    } else {
        // Preview-only mode: no encoder/downscale step — emit the post-blur
        // frame directly. (Optional MSTG path still applies; falls back to RPC.)
        emitPreview(frame);
    }
}

// Single point that delivers a processed VideoFrame to the preview consumer.
// MSTG path (`previewWriter` set) writes to the generator's WritableStream — the
// browser closes the frame after the writer takes ownership. RPC path (legacy
// fallback) hands the frame to `onPreviewFrame`, which serialises it across.
function emitPreview(frame: VideoFrame): void {
    if (previewWriter && !previewWriterClosed) {
        previewWriter.write(frame).catch((err: unknown) => {
            warnLog?.log('Preview MSTG write failed:', err);
            previewWriterClosed = true;
            try { frame.close(); } catch { /* already closed */ }
        });
    } else {
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
            // No blur, no encoder — just emit as preview (MSTG or RPC fallback)
            emitPreview(frame);
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

    // Track the live VideoFrame reference at each pipeline stage so the catch
    // path can close whatever's still owned. Without this, an exception between
    // sourceFrame creation and encoder.encode() leaks the intermediate frame
    // (browser console: `VideoFrame was garbage collected without being closed`).
    // The encoder wrapper closes its argument in finally, so once we hand off
    // to encode() this variable is set to null.
    let liveFrame: VideoFrame | null = frame;
    let sourceFrame: VideoFrame | null = null;
    try {
        if (startTimestamp === undefined) {
            startTimestamp = frame.timestamp;
            infoLog?.log(`Start timestamp set to ${startTimestamp}μs`);
        }

        // Normalize the source frame timestamp ONCE here, before downscaler
        // fan-out, so primary + simulcast extras share a single timeline and
        // emitted chunks carry matching offsets per source frame.
        // Math.round keeps ticks int64-safe — Chromium occasionally hands back
        // sub-µs fractions on MSTP-wrapped getUserMedia, which would otherwise
        // propagate to `Offset` and force msgpack float64 (server rejects).
        const normalizedSourceTs = Math.round(frame.timestamp - startTimestamp);
        sourceFrame = frame;
        if (normalizedSourceTs !== frame.timestamp) {
            sourceFrame = new VideoFrame(frame, {
                timestamp: normalizedSourceTs,
                duration: frame.duration ?? undefined,
            });
            frame.close();
            liveFrame = sourceFrame;
        }

        let processedFrame: VideoFrame;
        if (downscaler) {
            // WebGPU path: keeps frame on GPU. Uses VideoFrame.rotation when set,
            // else senderRotationDeg (main-thread supplies from screen.orientation).
            // downscaler.process closes its input internally; sourceFrame is gone.
            const results = downscaler.process(sourceFrame, senderRotationDeg);
            sourceFrame = null;
            processedFrame = results[0].frame;
            liveFrame = processedFrame;
            // Simulcast extras — feed each additional downscale result to its layer
            // encoder. Encoders stamp SpatialLayerId on every emitted chunk via their
            // ctor-bound id, so the fan-out path tags frames automatically. The
            // extra encoders close their input in finally regardless of success.
            if (extraLayerEncoders.length > 0) {
                for (let i = 0; i < extraLayerEncoders.length; i++) {
                    const extra = results[i + 1];
                    try {
                        extraLayerEncoders[i].encode(extra.frame, nextFrameIsKeyFrame);
                    } catch (e) {
                        errorLog?.log(`Extra layer ${i + 1} encode error:`, e);
                        try { extra.frame.close(); } catch { /* already closed */ }
                    }
                }
            }
            // Defensive cleanup: if `extras.length` is shorter than results - 1
            // (transient mismatch during a reconfig), close any orphan downscale
            // results so they don't get GC'd unclosed.
            for (let i = extraLayerEncoders.length + 1; i < results.length; i++) {
                try { results[i].frame.close(); } catch { /* already closed */ }
            }
        } else {
            const resized = resizeFrame(sourceFrame, encoderConfig.width, encoderConfig.height, resizeCanvas, resizeCtx, needsRotation);
            // resizeFrame closes its input when it produces a new frame; the
            // returned `frame` is the live one.
            sourceFrame = null;
            processedFrame = resized.frame;
            liveFrame = processedFrame;
            resizeCanvas = resized.canvas;
            resizeCtx = resized.ctx;
        }

        // WYSIWYG preview: clone the processed frame (post-rotate, post-downscale,
        // post-blur — exactly what we hand to the encoder) and emit to the preview
        // sink. Encoder still consumes the original `processedFrame` below; clone
        // is independent and closed by the MSTG writer / RPC consumer.
        if (previewWriter && !previewWriterClosed) {
            try {
                const previewClone = new VideoFrame(processedFrame, { timestamp: processedFrame.timestamp });
                emitPreview(previewClone);
            } catch (e) {
                if (!loggedPreviewCloneError) {
                    loggedPreviewCloneError = true;
                    warnLog?.log('Preview clone failed:', e);
                }
            }
        }

        // Optional YUV pre-conversion — disabled by default since HW encoders accept RGBA natively.
        // Enable via EncoderConfig.preConvertYuv for devices where pre-conversion helps encoding perf.
        // When the WebGPU downscaler is active its output is a GPU-resident
        // canvas-backed VideoFrame; running copyTo→new VideoFrame here forces a
        // GPU→CPU readback + re-upload, which is exactly the round-trip we
        // designed the downscaler path to avoid. Skip and let the HW encoder
        // consume RGBA directly.
        if (encoderConfig.preConvertYuv && downscaler) {
            if (!loggedPreConvertSkipped) {
                loggedPreConvertSkipped = true;
                warnLog?.log('preConvertYuv ignored: WebGPU downscaler active (output already GPU-resident, HW encoder consumes RGBA)');
            }
        }
        else if (encoderConfig.preConvertYuv) {
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
                        liveFrame = processedFrame;
                        converted = true;
                        break;
                    } catch { continue; }
                }

                if (!converted) {
                    try {
                        const result = cpuRgbaToI420(processedFrame, resizeCanvas, resizeCtx);
                        // cpuRgbaToI420 closes its input and returns a fresh I420 frame.
                        processedFrame = result.frame;
                        liveFrame = processedFrame;
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

        // Timestamp already normalized at source (pre-downscaler), so primary
        // and simulcast extras share one timeline here.

        // Let the encoder decide keyframes based on its keyframeInterval config
        // (set by recording-service.ts: ~2s screencast, ~3s webcam)
        const forceKf = nextFrameIsKeyFrame;
        nextFrameIsKeyFrame = false;
        // Cache a handle to the frame for the screencast heartbeat BEFORE
        // encoder.encode() — the encoder closes the frame it receives.
        // clone() shares underlying pixel data, so this is a ref bump not a copy.
        if (streamCtx.streamKind === 1) {
            if (lastEncodedFrame) lastEncodedFrame.close();
            lastEncodedFrame = processedFrame.clone();
            lastEncodedTimestampUs = processedFrame.timestamp;
            lastEncodedFrameAt = performance.now();
        }
        // Encoder.encode() closes processedFrame in its finally regardless of
        // success/throw — drop our live-tracking now so the catch below doesn't
        // try to close an already-closed frame.
        liveFrame = null;
        encoder.encode(processedFrame, forceKf);
        framesWithoutOutput++;

        // Detect dead encoder: error seen + 30 frames (~1s @ 30fps) with zero
        // output. Reduced from 90 (3s) — the retry-with-backoff in
        // WebCodecsEncoder.initialize() already gives the encoder ~1.5s to recover
        // from NVENC contention before reporting failure, so the watchdog only
        // needs to catch errors that slipped past retry (codec genuinely broken).
        if (encoderErrorSeen && framesWithoutOutput > 30 && !encoderFailed) {
            encoderFailed = true;
            const codec = encoderConfig.codec;
            errorLog?.log(`Encoder dead: ${codec} — ${framesWithoutOutput} frames with no output after error`);
            void callbacks.onEncoderFailed(codec, rpcNoWait);
        }
    } catch (error) {
        errorLog?.log('Error encoding frame:', error);
        // Whichever stage we got to, close the still-owned frame to avoid
        // GC-without-close warnings.
        if (liveFrame) {
            try { liveFrame.close(); } catch { /* already closed */ }
        }
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
            // Bump derived AVC level to admit the largest tier the ladder will encode.
            // Encoders pick the minimum level for THEIR input dims; the base layer
            // (e.g. 320×180) yields Level 3.0, but the 720p / 1080p simulcast extras
            // need Level 3.1 / 4.0. Without this bump the extra encoders fail with
            // `NotSupportedError ... AVC level (3.0) ... codec string (0x1E)`.
            const max = ladderMaxDims();
            const minLevelByte = pickAvcLevelByte(max.width, max.height);
            const derivedCodec = deriveAvcCodecFromDescription(descBuffer, minLevelByte);
            warnLog?.log(`Encoder output mismatch: configured=${encoderConfig!.codec} but output is avcC, correcting to ${derivedCodec} (ladder max ${max.width}x${max.height})`);
            actualCodec = derivedCodec;
            encoderConfig!.codec = derivedCodec;
        }
    }

    if (streamingEnabled) {
        deliverChunkToStream(chunkBuffer, chunkData.chunk.timestamp, chunkData.chunk.duration ?? 0,
            chunkData.type === 'key', actualCodec, chunkData.sequenceNumber, descBuffer,
            chunkData.temporalLayerId, chunkData.spatialLayerId,
            chunkData.width, chunkData.height);
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
    temporalLayerId?: number,
    spatialLayerId?: number,
    chunkWidth?: number,
    chunkHeight?: number
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
        // Causes: RPC peer-change (server restart / different hubId), or server
        // killed PushVideo via its frame-silence watchdog (WebcamFrameSilenceTimeout /
        // ScreencastFrameSilenceTimeout), or MaxLiveDuration. Recreate on next keyframe.
        // Reset startTimestamp so the next stream starts at offset 0 — avoids
        // handing the fresh server-side StreamStore a large non-zero baseline.
        warnLog?.log('VideoStream disposed — will recreate on next keyframe');
        videoStream = null;
        startTimestamp = undefined;
        streamStatus = 'reconnecting: waiting for keyframe';
    }

    const chunkData = new Uint8Array(chunkBytes);

    // Use encoder-instance dims when provided (simulcast extras differ from primary),
    // fall back to primary's encoderConfig for single-encoder streams.
    const frameWidth = chunkWidth ?? encoderConfig!.width;
    const frameHeight = chunkHeight ?? encoderConfig!.height;

    const frame: VideoStreamFrame = {
        offset: microsecondsToTicks(Math.round(timestamp)),
        duration: microsecondsToTicks(Math.round(duration)),
        isKeyFrame,
        width: frameWidth, height: frameHeight,
        data: chunkData, codec: isKeyFrame ? codec : undefined,
        temporalLayerId: temporalLayerId,
        spatialLayerId: spatialLayerId,
        // Source dims piggybacked on keyframes only — server uses them to
        // recompute its max-quality ceiling when the window is resized mid-stream.
        sourceWidth: isKeyFrame ? sourceWidth : undefined,
        sourceHeight: isKeyFrame ? sourceHeight : undefined,
    };

    if (isKeyFrame) {
        infoLog?.log(`Streaming keyframe: spatial=${spatialLayerId ?? 0}, temporal=${temporalLayerId ?? 0}, seq=${sequenceNumber}, ${frameWidth}x${frameHeight}, offsetMs=${(timestamp / 1000).toFixed(0)}, ${(chunkData.length / 1024).toFixed(2)} KB`);
    }

    // Description handling:
    // - Encoder emitted description on this keyframe → forward as-is, refresh per-layer cache.
    // - Encoder omitted description on this keyframe → fill from per-layer cache so the
    //   receiver's decoder can always reconfigure() (HEVC/AVC require description on every
    //   configure; Chrome is allowed by spec to omit it on later keyframes).
    // Keyed by spatialLayerId because HVCC/AVCC bytes differ per resolution in simulcast.
    if (isKeyFrame) {
        const layerId = spatialLayerId ?? 0;
        if (descriptionBytes && descriptionBytes.byteLength > 0) {
            const descBytes = new Uint8Array(descriptionBytes);
            frame.description = descBytes;
            storedDescriptionBytesByLayer.set(layerId, descBytes);

            if (!codecSettings) {
                let binary = '';
                for (const byte of descBytes) binary += String.fromCharCode(byte);
                codecSettings = btoa(binary);
                debugLog?.log(`Captured codec description for layer ${layerId}: ${descBytes.length} bytes`);
            }
        } else {
            const cached = storedDescriptionBytesByLayer.get(layerId);
            if (cached) {
                frame.description = cached;
            } else {
                warnLog?.log(`Keyframe for layer ${layerId} has no description and no cached entry`);
            }
        }
    }

    if (!videoStream) {
        const isAV1 = encoderConfig!.codec.startsWith('av01');
        const canCreateStream = codecSettings ?? (isAV1 && isKeyFrame);
        if (canCreateStream) {
            const settings = codecSettings ?? '';
            infoLog?.log(`Creating VideoStream: codec=${encoderConfig!.codec}, ${encoderConfig!.width}x${encoderConfig!.height}, codecSettings=${settings.length} chars`);
            videoStream = new InternalVideoStream(
                {
                    codec: encoderConfig!.codec,
                    width: encoderConfig!.width,
                    height: encoderConfig!.height,
                    sourceWidth: sourceWidth || encoderConfig!.width,
                    sourceHeight: sourceHeight || encoderConfig!.height,
                    codecSettings: settings,
                },
                streamCtx,
                lastVideoStream?.whenDisposed,
            );
            lastVideoStream = videoStream;
            streamRecreations++;
            warnLog?.log(`TIMING_ANCHOR: startTimestamp=${((startTimestamp ?? 0) / 1000).toFixed(0)}ms, firstChunkOffsetMs=${(timestamp / 1000).toFixed(0)}`);
            for (const buffered of pendingStreamFrames) videoStream.addFrame(buffered);
            pendingStreamFrames = [];
            videoStream.addFrame(frame);
            streamStatus = 'streaming';
            void callbacks.onStreamCreated(settings, rpcNoWait);
        } else {
            streamStatus = 'waiting for codec description';
            pendingStreamFrames.push(frame);
        }
    } else {
        videoStream.addFrame(frame);
    }
}

function onEncoderError(error: Error): void {
    errorLog?.log('Encoder error:', error.name, error.message);
    encoderErrorSeen = true;
    lastStreamError = `encoder: ${error.name} ${error.message}`;
}

// Try to reuse a pooled encoder. Returns true if reused. Match by codec
// CATEGORY (av1/hevc/vp9/h264), not exact codec string — HEVC L3.1 vs L4.0
// (or AVC level changes) share the same NVENC session and `configure()` does
// a level reconfigure on the existing instance. Different category → close
// pool, create new (NVENC release will overlap with new acquire — best we can
// do without keeping multiple slots).
function tryAdoptPooledPrimary(targetCodec: string): boolean {
    if (poolExpireTimer !== null) {
        clearTimeout(poolExpireTimer);
        poolExpireTimer = null;
    }
    if (!pooledPrimary) return false;
    const poolCategory = getCodecCategory(pooledPrimary.codec);
    const targetCategory = getCodecCategory(targetCodec);
    if (poolCategory !== targetCategory) {
        // Pool entry is wrong codec family — close it. Frees slot (eventually).
        infoLog?.log(`Encoder pool: category mismatch (pool=${poolCategory}/${pooledPrimary.codec}, want=${targetCategory}/${targetCodec}), evicting`);
        try { pooledPrimary.encoder.close(); } catch { /* ignore */ }
        pooledPrimary = null;
        return false;
    }
    // Same codec family — reuse. Caller updates encoder.setConfig() with the
    // new pipeline's config (possibly different level/dims/bitrate), then
    // initializeEncoders() calls configure() which is a reconfigure on the
    // existing NVENC session — no new HW slot acquired.
    infoLog?.log(`Encoder pool: reusing primary (${pooledPrimary.codec} ${pooledPrimary.width}x${pooledPrimary.height} → ${targetCodec})`);
    encoder = pooledPrimary.encoder;
    encoder.reset();
    pooledPrimary = null;
    return true;
}

// Park the primary encoder for reuse on next start. Closes extras (per-session).
// TTL timer evicts the pool if no restart within POOL_TTL_MS.
function parkPrimaryEncoder(): void {
    if (encoder && encoderConfig) {
        // Evict any stale pool entry (shouldn't happen but be safe).
        if (pooledPrimary) {
            try { pooledPrimary.encoder.close(); } catch { /* ignore */ }
        }
        pooledPrimary = {
            encoder,
            codec: encoderConfig.codec,
            width: encoderConfig.width,
            height: encoderConfig.height,
        };
        infoLog?.log(`Encoder pool: parked primary (${encoderConfig.codec} ${encoderConfig.width}x${encoderConfig.height}), TTL ${POOL_TTL_MS}ms`);
        encoder = null;
        if (poolExpireTimer !== null) clearTimeout(poolExpireTimer);
        poolExpireTimer = setTimeout(() => {
            poolExpireTimer = null;
            if (pooledPrimary) {
                infoLog?.log('Encoder pool: TTL expired, closing primary');
                try { pooledPrimary.encoder.close(); } catch { /* ignore */ }
                pooledPrimary = null;
            }
        }, POOL_TTL_MS);
    }
    // Always close extras — simulcast configs are per-session.
    for (const e of extraLayerEncoders) {
        try { e.close(); } catch { /* already closed */ }
    }
    extraLayerEncoders = [];
}

// Creates the base encoder (SpatialLayerId=0) plus any simulcast extras declared
// in `config.spatialLayers`. Base encoder always present; extras empty in P2P mode.
function setupEncoders(config: VideoProcessingConfig): void {
    encoderConfig = config.encoder;
    // Fresh encoder instances emit fresh HVCC/AVCC on their first keyframe — drop
    // any cached descriptions from prior encoder generation to avoid stale bytes.
    storedDescriptionBytesByLayer.clear();
    // Reuse pooled encoder if codec matches — keeps NVENC session held across
    // stop/start, avoiding `OperationError: Encoder initialization error` from
    // the previous session's slow async release.
    if (tryAdoptPooledPrimary(config.encoder.codec)) {
        // Update the pooled encoder's config to the new pipeline's params.
        // initializeEncoders() will call configure() with these dims/bitrate.
        encoder!.setConfig(config.encoder);
    } else {
        encoder = new WebCodecsEncoder(config.encoder, onEncoderOutput, onEncoderError, 0);
    }

    // Drop any prior extras (covers switchCodec re-entry and re-init scenarios).
    for (const e of extraLayerEncoders) {
        try { e.close(); } catch { /* already closed */ }
    }
    extraLayerEncoders = [];

    const layers = config.spatialLayers ?? [];
    for (let i = 0; i < layers.length; i++) {
        const layer = layers[i];
        const layerCfg: EncoderConfig = {
            ...config.encoder,
            width: layer.width,
            height: layer.height,
            bitrate: layer.bitrate,
            scalabilityMode: layer.scalabilityMode ?? config.encoder.scalabilityMode,
        };
        const spatialId = i + 1; // base layer consumed index 0
        const extra = new WebCodecsEncoder(layerCfg, onEncoderOutput, onEncoderError, spatialId);
        extraLayerEncoders.push(extra);
        infoLog?.log(`Simulcast layer ${spatialId}: ${layer.width}x${layer.height} @ ${(layer.bitrate / 1_000_000).toFixed(1)}Mbps`);
    }
    // .initialize() (encoder.configure) deferred to first frame so the first
    // configure call uses post-reconcile dims — see initializeEncoders().
    encodersInitialized = false;
}

// Calls .initialize() (encoder.configure) on the base + extra encoders.
// Idempotent. Stream-mode callers invoke this after first-frame dim reconcile
// so the very first configure call lands on final dims; RPC fallback mode
// invokes it eagerly during start-up since it has no first-frame reconcile.
function initializeEncoders(): void {
    if (encodersInitialized) return;
    encodersInitialized = true;
    if (!encoder) return;
    try { encoder.initialize(); } catch { /* error already surfaced via onEncoderError */ }
    for (const e of extraLayerEncoders) {
        try { e.initialize(); } catch { /* error already surfaced via onEncoderError */ }
    }
}

// Largest (width, height) across base + extras. Used to pick an AVC level
// that admits every layer in the ladder — see deriveAvcCodecFromDescription
// site for why a base-derived level is too low for simulcast.
function ladderMaxDims(): { width: number; height: number } {
    let w = encoderConfig?.width ?? 0;
    let h = encoderConfig?.height ?? 0;
    for (const e of extraLayerEncoders) {
        const s = e.getStats();
        if (s.configuredWidth > w) w = s.configuredWidth;
        if (s.configuredHeight > h) h = s.configuredHeight;
    }
    return { width: w, height: h };
}

// Cheap structural match: same length AND identical (w, h, bitrate) per index.
// Used by setSpatialLayers to skip a no-op rebuild — repeated server pushes of
// the same ladder are common and we don't want to drain the encoder pipeline
// on every duplicate.
function extraLayerCountMatches(layers: SpatialLayerConfig[]): boolean {
    if (layers.length !== extraLayerEncoders.length) return false;
    for (let i = 0; i < layers.length; i++) {
        const live = extraLayerEncoders[i].getStats();
        const want = layers[i];
        if (live.configuredWidth !== want.width
            || live.configuredHeight !== want.height
            || live.configuredBitrate !== want.bitrate) return false;
    }
    return true;
}

function collectDownscaleTargets(config: VideoProcessingConfig): DownscaleTarget[] {
    return [
        { width: config.encoder.width, height: config.encoder.height },
        ...(config.spatialLayers ?? []).map(l => ({ width: l.width, height: l.height })),
    ];
}

// Builds the current downscaler target list from live encoder state. Primary
// dims come from encoderConfig (which is mutated by reconfigure + orientation
// reconcile); extras come from each live WebCodecsEncoder's configured dims.
// Used by any code path that rebuilds downscaler config mid-stream so that
// simulcast extras aren't accidentally dropped.
function currentDownscaleTargets(): DownscaleTarget[] {
    const targets: DownscaleTarget[] = [];
    if (encoderConfig) targets.push({ width: encoderConfig.width, height: encoderConfig.height });
    for (const e of extraLayerEncoders) {
        const s = e.getStats();
        targets.push({ width: s.configuredWidth, height: s.configuredHeight });
    }
    return targets;
}

// Init WebGPU downscaler for the full simulcast target list (base + any extras).
// Returns null if WebGPU is unavailable — caller then relies on the legacy canvas
// resizeFrame path (which only covers the primary layer; simulcast requires WebGPU).
async function initDownscaler(config: VideoProcessingConfig): Promise<void> {
    const targets = collectDownscaleTargets(config);
    if (downscaler) {
        downscaler.configure(targets);
        return;
    }
    try {
        const device = await WebGPUManager.init();
        downscaler = new WebGpuDownscaler(device);
        downscaler.configure(targets);
        infoLog?.log(`Downscaler initialized with ${targets.length} target(s)`);
    } catch (e) {
        warnLog?.log('WebGPU downscaler unavailable, falling back to canvas resize:', e);
        downscaler = null;
    }
}

// ─── Preview-only (MSTG) helpers ────────────────────────────────────────────

interface PreviewMSTG { writable: WritableStream<VideoFrame>; track: MediaStreamTrack }

// Initialise the preview MSTG and hand the output track to main. Idempotent —
// no-op if MSTG already set up. Returns true on success. Encoder modes call
// this opportunistically (no-op on browsers without MSTG, RPC fallback applies);
// previewOnly mode calls it as a hard requirement.
function ensurePreviewMSTG(): boolean {
    if (previewWriter && !previewWriterClosed) return true;
    const mstg = tryCreatePreviewMSTG();
    if (!mstg) return false;
    previewWriter = mstg.writable.getWriter();
    previewWriterClosed = false;
    void callbacks.onPreviewTrack(mstg.track, rpcNoWait);
    return true;
}

function tryCreatePreviewMSTG(): PreviewMSTG | null {
    const w = self as unknown as {
        VideoTrackGenerator?: VideoTrackGeneratorCtor;
        MediaStreamTrackGenerator?: MediaStreamTrackGeneratorCtor;
    };
    try {
        if (typeof w.VideoTrackGenerator === 'function') {
            const gen = new w.VideoTrackGenerator();
            return { writable: gen.writable, track: gen.track };
        }
        if (typeof w.MediaStreamTrackGenerator === 'function') {
            const gen = new w.MediaStreamTrackGenerator({ kind: 'video' });
            return { writable: gen.writable, track: gen.track };
        }
    } catch (e) {
        warnLog?.log('Failed to construct preview MSTG:', e);
    }
    return null;
}

async function previewReadLoop(inputReader: ReadableStreamDefaultReader<VideoFrame>): Promise<void> {
    try {
        while (processing) {
            const { done, value } = await inputReader.read();
            if (done) { infoLog?.log('Preview input ended'); break; }
            processOneFrame(value);
        }
    } catch (e) {
        errorLog?.log('previewReadLoop error:', e);
    } finally {
        try { inputReader.releaseLock(); } catch { /* ignore */ }
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
                sourceWidth = frameW;
                sourceHeight = frameH;
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
                        // (avoids upscaling which wastes CPU for no quality gain).
                        // Ensure even dims — video codecs require it; odd window/tab
                        // capture sizes would cause an encoder error.
                        const evenW = frameW & ~1;
                        const evenH = frameH & ~1;
                        warnLog?.log(`Display dimensions ${frameW}x${frameH} smaller than config ${encoderConfig.width}x${encoderConfig.height}, reconfiguring to ${evenW}x${evenH}`);
                        encoderConfig.width = evenW; encoderConfig.height = evenH;
                        // Downscaler first (sync), encoder second (async) — same invariant as
                        // orientation/RPC reconfigure paths. Without this the downscaler keeps
                        // its old slot target and feeds frames at the old dims into a smaller
                        // encoder, hitting the dim-mismatch guard and dropping every frame.
                        if (downscaler) downscaler.configure(currentDownscaleTargets());
                        if (encodersInitialized) {
                            await encoder.reconfigure({ width: evenW, height: evenH, bitrate: encoderConfig.bitrate });
                        }
                        if (segConfig) { segConfig.outputWidth = evenW; segConfig.outputHeight = evenH; }
                        void callbacks.onDimensionReconciled(evenW, evenH, rpcNoWait);
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
                // Track running max source dimensions so window resize / camera swap
                // that grows the source is visible to the server via subsequent
                // keyframes (see enqueueStreamingFrame — source dims attached on KF).
                const currentW = rawFrame.displayWidth;
                const currentH = rawFrame.displayHeight;
                if (currentW > sourceWidth) sourceWidth = currentW;
                if (currentH > sourceHeight) sourceHeight = currentH;
            }

            // Determine user-facing orientation from POST-rotation source dims,
            // not from the rotation value alone. Per-platform rationale:
            //   - iOS Safari MSTP: source landscape (1920×1080), `frame.rotation`
            //     null → fallback `senderRotationDeg=90` → swap → portrait. ✓
            //   - Android Chrome MSTP: source already-portrait (720×1280) when
            //     phone is portrait, `frame.rotation=0` → no swap → portrait. ✓
            //     (The old `displayPortrait = rotDeg === 90 || === 270` check
            //     misread Android's `rotation=0` as "wants landscape" and flipped
            //     the encoder mid-stream.)
            //   - Desktop: source landscape (1280×720), rotation=0 → no swap →
            //     landscape. Reconcile flips encoder away from the iOS-tuned
            //     startup-portrait transpose, as before.
            const rotDeg = frameRotation ?? senderRotationDeg;
            let finalW = rawFrame.displayWidth;
            let finalH = rawFrame.displayHeight;
            if (rotDeg === 90 || rotDeg === 270) {
                const t = finalW; finalW = finalH; finalH = t;
            }
            const displayPortrait = finalH > finalW;
            const encoderPortrait = encoderConfig.height > encoderConfig.width;
            if (displayPortrait !== encoderPortrait) {
                const newW = encoderConfig.height;
                const newH = encoderConfig.width;
                try {
                    encoderConfig.width = newW;
                    encoderConfig.height = newH;
                    // Downscaler first (sync) — see comment in `reconfigure` RPC
                    // above about the race window during encoder.reconfigure's
                    // `await` and why downscaler-first + dims-mismatch guard
                    // beats the Chrome HW-encoder top-left-crop fallback.
                    if (downscaler) downscaler.configure(currentDownscaleTargets());
                    // First-frame: encoder.configure() is deferred (encodersInitialized=false)
                    // so we just mutate encoderConfig — initializeEncoders() below will pick
                    // up the final dims. Mid-stream rotation flip (encoder already warm) hits
                    // the encoder.reconfigure() branch — by then HW encoder accepts it.
                    if (encodersInitialized) {
                        await encoder.reconfigure({ width: newW, height: newH, bitrate: encoderConfig.bitrate });
                    }
                    if (segConfig) { segConfig.outputWidth = newW; segConfig.outputHeight = newH; }
                    void callbacks.onDimensionReconciled(newW, newH, rpcNoWait);
                    infoLog?.log(`Orientation change: rotation=${rotDeg} → encoder ${newW}x${newH} (downscaler targets=${extraLayerEncoders.length + 1})`);
                } catch (e) {
                    warnLog?.log('Orientation reconfigure failed:', e);
                }
            }

            // Lazy-initialize encoders on first frame, after dim reconcile mutated
            // encoderConfig.width/height to its final values. This is the SINGLE
            // configure() call — no flip-flop, no HW encoder init crash on HEVC.
            if (!encodersInitialized) initializeEncoders();

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

function startScreencastHeartbeat(): void {
    if (heartbeatTimer) return;
    if (streamCtx.streamKind !== 1) return; // screencast only
    heartbeatTimer = setInterval(() => {
        if (!processing || !encoder || !encoderConfig) return;
        if (!videoStream || videoStream.isDisposed) return;
        if (!lastEncodedFrame) return;
        const silence = performance.now() - lastEncodedFrameAt;
        if (silence < SCREENCAST_HEARTBEAT_MS) return;

        try {
            // Monotonic timestamps: advance by the wall-clock gap since the last
            // frame we fed to the encoder (real or heartbeat). μs.
            const freshTs = lastEncodedTimestampUs + Math.max(1, Math.round(silence * 1000));
            // Constructing a new VideoFrame from lastEncodedFrame shares the
            // underlying pixel buffer (ref bump). The encoder closes `fresh`
            // after encode() — but lastEncodedFrame stays alive and reusable
            // for the next tick. Do NOT close+re-clone lastEncodedFrame here;
            // that would fail on the next tick because encoder.encode() has
            // already closed `fresh` before we'd attempt fresh.clone().
            const fresh = new VideoFrame(lastEncodedFrame, {
                timestamp: freshTs,
                duration: lastEncodedFrame.duration ?? undefined,
            });
            // Don't force keyframe here — encoder auto-promotes every
            // maxKeyFrameIntervalMs. P-frame on identical content is near-zero
            // bytes; keyframes land on schedule for mid-stream joiners.
            encoder.encode(fresh, false);
            lastEncodedTimestampUs = freshTs;
            lastEncodedFrameAt = performance.now();
            debugLog?.log(`Screencast heartbeat: re-encoded last frame after ${Math.round(silence)}ms of silence`);
        } catch (e) {
            warnLog?.log('Screencast heartbeat encode failed:', e);
        }
    }, SCREENCAST_HEARTBEAT_CHECK_MS);
}

function stopScreencastHeartbeat(): void {
    if (heartbeatTimer) { clearInterval(heartbeatTimer); heartbeatTimer = null; }
    if (lastEncodedFrame) { lastEncodedFrame.close(); lastEncodedFrame = null; }
    lastEncodedFrameAt = 0;
    lastEncodedTimestampUs = 0;
}

// ─── Server implementation ──────────────────────────────────────────────────

export const serverImpl: VideoProcessingWorker = {

    startWithStream: async (config, frameInputStream): Promise<void> => {
        try {
            infoLog?.log('Starting video processing worker (stream mode)...');
            applyStreamingConfig(config);
            senderRotationDeg = config.senderRotationDeg ?? 0;

            // Set up MSTG output BEFORE encoder so the preview track is delivered
            // to main while we're still in the awaited startup window — main can
            // read it synchronously after `startWithStream` resolves. No-op
            // (returns false) on browsers without MSTG; preview falls back to
            // the existing RPC `onPreviewFrame` callback path.
            ensurePreviewMSTG();

            if (config.segmentation) {
                await initializeSegmentation({ ...config.segmentation, outputWidth: config.encoder.width, outputHeight: config.encoder.height });
                blurEnabled = true;
                infoLog?.log('Segmentation initialized (blur enabled)');
            }

            setupEncoders(config);
            await initDownscaler(config);

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
            startScreencastHeartbeat();

            infoLog?.log('Video processing worker started (stream mode)');
        } catch (error) {
            errorLog?.log('Failed to start stream mode:', error);
            throw error;
        }
    },

    startPreviewWithTrack: async (config, track): Promise<void> => {
        try {
            infoLog?.log('Starting preview-only worker (MSTG mode)...');

            if (typeof MediaStreamTrackProcessor === 'undefined')
                throw new Error('MediaStreamTrackProcessor not available in worker scope');

            // Hand the output track to main BEFORE we start producing frames so
            // the consumer is wired up by the time the first frame arrives.
            if (!ensurePreviewMSTG())
                throw new Error('MediaStreamTrackGenerator/VideoTrackGenerator not available in worker scope');

            senderRotationDeg = config.senderRotationDeg ?? 0;
            if (config.segmentation) {
                await initializeSegmentation({
                    ...config.segmentation,
                    outputWidth: config.encoder.width,
                    outputHeight: config.encoder.height,
                });
                blurEnabled = true;
            }

            processing = true;
            inputTrack = track;

            const processor = new MediaStreamTrackProcessor({ track });
            const inputReader = processor.readable.getReader();
            streamReadLoopPromise = previewReadLoop(inputReader);

            infoLog?.log('Preview-only worker started (MSTG mode)');
        } catch (error) {
            errorLog?.log('Failed to start preview-only worker (MSTG mode):', error);
            // Roll back partial state so the caller can fall back cleanly.
            if (previewWriter) {
                try { await previewWriter.close(); } catch { /* ignore */ }
                previewWriter = null;
            }
            previewWriterClosed = false;
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

            // Set up MSTG output BEFORE encoder so the preview track is delivered
            // to main while we're still in the awaited startup window.
            ensurePreviewMSTG();

            if (config.segmentation) {
                await initializeSegmentation({ ...config.segmentation, outputWidth: config.encoder.width, outputHeight: config.encoder.height });
                blurEnabled = true;
                infoLog?.log('Segmentation initialized (blur enabled)');
            }

            setupEncoders(config);
            await initDownscaler(config);

            if (config.adaptiveFramerate) {
                vadReducedFrameIntervalMs = 1000 / config.adaptiveFramerate.reducedFps;
            }

            processing = true;
            streamCtx.processing = true;
            dimensionsReconciled = false;
            needsRotation = false;
            orientationStats = null;
            inputTrack = track;

            const processor = new MediaStreamTrackProcessor({ track });
            const inputReader = processor.readable.getReader();
            streamReadLoopPromise = streamReadLoop(inputReader);
            startScreencastHeartbeat();

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
                // Encoder mode: opportunistic MSTG output. No-op on browsers
                // without MSTG; preview falls back to RPC `onPreviewFrame`.
                ensurePreviewMSTG();
            }
            senderRotationDeg = config.senderRotationDeg ?? 0;

            if (config.segmentation) {
                await initializeSegmentation({ ...config.segmentation, outputWidth: config.encoder.width, outputHeight: config.encoder.height });
                blurEnabled = true;
            }

            if (!isPreviewOnly) {
                setupEncoders(config);
                await initDownscaler(config);
                // RPC fallback: no streamReadLoop first-frame reconcile fires here,
                // so the encoder must be configured eagerly with the supplied dims.
                initializeEncoders();
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
        params.width = (isPortrait ? inSmall : inLarge) & ~1;
        params.height = (isPortrait ? inLarge : inSmall) & ~1;
        infoLog?.log(`Reconfigure: ${params.bitrate / 1_000_000}Mbps, ${params.width}x${params.height}`);
        encoderConfig.bitrate = params.bitrate; encoderConfig.width = params.width; encoderConfig.height = params.height;
        resizeCanvas = null; resizeCtx = null;
        // Order matters. `encoder.reconfigure` has an `await` boundary, so the
        // stream-read loop can interleave a frame between the downscaler and
        // encoder config updates. We reconfigure the downscaler FIRST (sync)
        // so subsequent frames immediately land at the new target dims. Any
        // frame that still sneaks through while the encoder is mid-reconfigure
        // hits the dims-mismatch guard in `WebCodecsEncoder.encode` and is
        // dropped — preferable to letting Chrome's HW encoder silently
        // top-left-crop an old-dim frame against the new config.
        if (downscaler) {
            // Preserve simulcast extras when reconfiguring the primary layer.
            // Extras stay at their initial dims — per-layer reconfigure is a
            // later-stage feature driven by VideoQualityPreset.MaxSpatialLayer.
            downscaler.configure(currentDownscaleTargets());
        }
        await encoder.reconfigure(params);
        if (segConfig && blurEnabled) { segConfig.outputWidth = params.width; segConfig.outputHeight = params.height; }
        // Drop cached heartbeat frame — its dimensions no longer match the encoder.
        if (lastEncodedFrame) { lastEncodedFrame.close(); lastEncodedFrame = null; }
    },

    switchCodec: async (config: EncoderConfig, spatialLayers?: SpatialLayerConfig[]): Promise<void> => {
        if (!encoder) { warnLog?.log('Cannot switch codec: not active'); return; }
        infoLog?.log(`Switching codec to ${config.codec}`);
        streamStatus = 'switching codec';

        // Suppress frame output during codec switch — encoder.switchCodec() flushes
        // old-codec frames that must NOT leak into the new stream
        const wasStreaming = streamingEnabled;
        streamingEnabled = false;

        if (videoStream) {
            videoStream.complete();
            try { await videoStream.whenDisposed; } catch { /* ignore */ }
            videoStream = null;
        }
        codecSettings = null; startTimestamp = undefined; pendingStreamFrames = []; storedDescriptionBytesByLayer.clear();
        if (lastEncodedFrame) { lastEncodedFrame.close(); lastEncodedFrame = null; }
        // Synchronous configure() failure inside switchCodec already surfaces via
        // onEncoderError; the watchdog plus codec-exclusion list takes care of
        // the next fallback. Don't let a sync throw break the worker RPC.
        let switchFailed = false;
        try { await encoder.switchCodec(config); } catch { switchFailed = true; /* error already surfaced via onEncoderError */ }
        // Drop old-codec simulcast extras; we rebuild them below with the new codec
        // so the ladder survives the switch. Without this rebuild every codec
        // switch permanently collapses the stream to base layer (regression
        // observed on AV1→H.264 audience changes: receivers stuck at 640×360).
        if (extraLayerEncoders.length > 0) {
            const extras = extraLayerEncoders.slice();
            extraLayerEncoders = [];
            infoLog?.log(`switchCodec: tearing down ${extras.length} simulcast extras`);
            for (const e of extras) {
                try { await e.flush(); } catch { /* already closed elsewhere */ }
                try { e.close(); } catch { /* already closed */ }
            }
        }

        encoderConfig = config; resizeCanvas = null; resizeCtx = null;
        // Rebuild simulcast extras on the new codec and restore downscaler targets
        // to match. If caller omits spatialLayers (P2P mode) we fall through to
        // a single base target and stay single-layer.
        const layers = spatialLayers ?? [];
        for (let i = 0; i < layers.length; i++) {
            const layer = layers[i];
            const layerCfg: EncoderConfig = {
                ...config,
                width: layer.width,
                height: layer.height,
                bitrate: layer.bitrate,
                scalabilityMode: layer.scalabilityMode ?? config.scalabilityMode,
            };
            const spatialId = i + 1;
            const extra = new WebCodecsEncoder(layerCfg, onEncoderOutput, onEncoderError, spatialId);
            try { extra.initialize(); } catch { /* error already surfaced via onEncoderError */ }
            extraLayerEncoders.push(extra);
            infoLog?.log(`Simulcast layer ${spatialId} (post-switch): ${layer.width}x${layer.height} @ ${(layer.bitrate / 1_000_000).toFixed(1)}Mbps`);
        }
        if (downscaler) downscaler.configure(currentDownscaleTargets());

        // Re-enable streaming and clear any frames that leaked during flush
        streamingEnabled = wasStreaming;
        // Diagnostic: explain what the encoder needs to resume streaming.
        // lastEncodedFrame is always null here (cleared above), so screencast
        // heartbeat can't fire until getDisplayMedia produces a new frame.
        // On a static screen this means the stream is stalled until user activity.
        if (streamCtx.streamKind === 1)
            streamStatus = 'stalled: waiting for screen activity after codec switch (heartbeat source lost)';
        else
            streamStatus = 'waiting for encoder output';
        pendingStreamFrames = [];
        startTimestamp = undefined;
        backpressureDrops = 0; backpressureTotalFrames = 0; lastBackpressureCheckTime = 0; backpressureNotified = false;
        // Always reset encoderFailed and framesWithoutOutput on codec switch — the
        // new codec attempt deserves its own watchdog cycle. If switchCodec failed
        // synchronously, keep encoderErrorSeen=true so the watchdog fires again
        // for the new codec; otherwise clear it for a normal warmup window.
        encoderFailed = false; framesWithoutOutput = 0;
        if (!switchFailed) {
            encoderErrorSeen = false; lastStreamError = '';
        }
        infoLog?.log(`Codec switched ${switchFailed ? 'with error' : 'successfully'} (${extraLayerEncoders.length} simulcast extras rearmed)`);
    },

    // Hot simulcast reconfig: swap the extra-layer encoders without touching the
    // base encoder or the active VideoStream/RPC connection. Used to enable
    // simulcast mid-recording when a second peer joins (or to drop it when the
    // call collapses to P2P), without the stop/start cascade documented in
    // commit 2de3f2617. No-op when the requested ladder matches the live one.
    setSpatialLayers: async (layers): Promise<void> => {
        if (!encoder || !encoderConfig) {
            warnLog?.log('Cannot set spatial layers: not active');
            return;
        }
        if (extraLayerCountMatches(layers)) {
            debugLog?.log(`setSpatialLayers: no-op, ${extraLayerEncoders.length} extras already match request`);
            return;
        }

        infoLog?.log(`setSpatialLayers: rebuilding ${extraLayerEncoders.length} → ${layers.length} extra(s)`);
        if (extraLayerEncoders.length > 0) {
            const extras = extraLayerEncoders.slice();
            extraLayerEncoders = [];
            for (const e of extras) {
                try { await e.flush(); } catch { /* already closed */ }
                try { e.close(); } catch { /* already closed */ }
            }
        }

        // If base codec is avc1.* and incoming extras need a higher AVC level
        // than the current string admits, bump it. Without this, hot-adding a
        // 720p extra to a base whose codec was auto-corrected to avc1.64001e
        // (Level 3.0, max 720×576) would fail with NotSupportedError.
        if (encoderConfig.codec.startsWith('avc1.') && layers.length > 0) {
            let maxW = encoderConfig.width;
            let maxH = encoderConfig.height;
            for (const l of layers) {
                if (l.width > maxW) maxW = l.width;
                if (l.height > maxH) maxH = l.height;
            }
            const needed = pickAvcLevelByte(maxW, maxH);
            const currentLevel = parseInt(encoderConfig.codec.slice(-2), 16);
            if (currentLevel < needed) {
                const bumped = encoderConfig.codec.slice(0, -2) + needed.toString(16).padStart(2, '0');
                infoLog?.log(`setSpatialLayers: bumping AVC level ${encoderConfig.codec} → ${bumped} for ladder max ${maxW}x${maxH}`);
                encoderConfig.codec = bumped;
            }
        }

        const baseCodec = encoderConfig.codec;
        for (let i = 0; i < layers.length; i++) {
            const layer = layers[i];
            const layerCfg: EncoderConfig = {
                ...encoderConfig,
                width: layer.width,
                height: layer.height,
                bitrate: layer.bitrate,
                scalabilityMode: layer.scalabilityMode ?? encoderConfig.scalabilityMode,
            };
            const spatialId = i + 1;
            const extra = new WebCodecsEncoder(layerCfg, onEncoderOutput, onEncoderError, spatialId);
            try { extra.initialize(); } catch { /* error already surfaced via onEncoderError */ }
            extraLayerEncoders.push(extra);
            infoLog?.log(`Simulcast layer ${spatialId} (hot): ${layer.width}x${layer.height} @ ${(layer.bitrate / 1_000_000).toFixed(1)}Mbps codec=${baseCodec}`);
        }
        if (downscaler) downscaler.configure(currentDownscaleTargets());

        // Force a keyframe so subscribers can latch onto any newly-armed layer
        // without waiting for the next encoder-driven keyframe interval.
        nextFrameIsKeyFrame = true;
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
        // Snapshot extras before iterating — switchCodec can reset the shared
        // `extraLayerEncoders` array concurrently, which would turn indexed
        // access into `undefined`.
        const extras = extraLayerEncoders.slice();
        for (let i = 0; i < extras.length; i++) {
            try { await extras[i].flush(); } catch (e) { warnLog?.log(`Extra layer ${i + 1} flush error:`, e); }
        }
    },

    stop: async (): Promise<void> => {
        infoLog?.log('Stopping video processing worker...');
        processing = false;
        streamCtx.processing = false;
        stopScreencastHeartbeat();

        if (streamReadLoopPromise) { try { await streamReadLoopPromise; } catch { /* ignore */ } streamReadLoopPromise = null; }
        if (previewWriter) {
            previewWriterClosed = true;
            try { await previewWriter.close(); } catch { /* writer may already be in error state */ }
            previewWriter = null;
        }
        if (inputTrack) {
            // Stop the cloned camera track explicitly — releases the camera light
            // immediately rather than waiting for MSTP/processor GC.
            try { inputTrack.stop(); } catch { /* ignore */ }
            inputTrack = null;
        }
        try { await awaitAllPendingReadbacks(); } catch { /* ignore */ }
        if (frameQueue) { while (!frameQueue.isEmpty()) { const qf = frameQueue.shift(); if (qf) qf.frame.close(); } frameQueue = null; }
        // Flush extras first (per-session simulcast — close fully). Primary
        // gets flushed and parked below for reuse on next start.
        // Snapshot + clear BEFORE awaiting flush — if switchCodec runs during
        // these awaits it also drains the shared array, and concurrent indexed
        // iteration would hit `undefined`. Flush and close are guarded
        // separately so a stale `flush()` rejecting with AbortError doesn't
        // skip the `close()` step.
        const extras = extraLayerEncoders.slice();
        extraLayerEncoders = [];
        for (let i = 0; i < extras.length; i++) {
            const e = extras[i];
            try { await e.flush(); } catch (err) { warnLog?.log(`Extra layer ${i + 1} flush error (stop):`, err); }
            try { e.close(); } catch (err) { warnLog?.log(`Extra layer ${i + 1} close error:`, err); }
        }
        // Park primary encoder for reuse on next start — keeps NVENC session
        // held across the stop/start gap. parkPrimaryEncoder also clears any
        // remaining extras (defensive — array should already be empty above).
        if (encoder) {
            try { await encoder.flush(); } catch (e) { warnLog?.log('Encoder flush error (stop):', e); }
            parkPrimaryEncoder();
        }
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
        encoder = null; encoderConfig = null; encodersInitialized = false; onnxSession = null; segConfig = null; resolvedModelConfig = null;
        segInitialized = false; blurEnabled = false; resizeCanvas = null; resizeCtx = null;
        startTimestamp = undefined; lastLoggedFormat = '(unset)'; loggedI420Error = false; loggedPreConvertSkipped = false;
        backpressureDrops = 0; backpressureTotalFrames = 0; lastBackpressureCheckTime = 0; backpressureNotified = false;
        dimensionsReconciled = false; needsRotation = false; orientationStats = null; sourceWidth = 0; sourceHeight = 0; vadSpeaking = true; vadRemoteStreamCount = 0; vadLastPassedFrameTime = 0;
        segFrameCounter = 0; hasValidMask = false; loggedBlurFormat = false; processingFrame = false; frameSequence = 0;
        segProcessedFrames = 0; segTotalInferenceTime = 0; segTotalBlurTime = 0; segTotalProcessingTime = 0; segDroppedFrames = 0;
        videoStream = null; lastVideoStream = null; pendingStreamFrames = [];
        codecSettings = null; storedDescriptionBytesByLayer.clear();
        streamingEnabled = false; streamRecreations = 0; streamStatus = 'idle'; lastStreamError = '';

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
        // Pick the first non-empty error string from current stream, previous stream, or worker-level
        const streamErrors = [videoStream?.lastError, lastVideoStream?.lastError, lastStreamError];
        const streamError = streamErrors.find(e => e != null && e.length > 0) ?? '';
        const streamStats: VideoProcessingStreamingStats | null = streamingEnabled ? {
            sentFrames: videoStream?.getAddedFrameCount() ?? 0,
            pendingFrames: pendingStreamFrames.length,
            streamRecreations,
            status: streamStatus,
            lastError: streamError,
        } : null;
        return { encoder: encoderStats, segmentation: segStats, orientation: orientationStats ? { ...orientationStats } : null, streaming: streamStats };
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
