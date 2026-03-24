/**
 * Unified Video Processing Worker
 *
 * Combines segmentation (background blur), encoding, and SignalR streaming
 * in a single worker. All VideoFrame processing stays within one context —
 * no cross-worker VideoFrame transfer needed.
 *
 * Architecture:
 *   Main → MSTP ReadableStream → this worker:
 *     ├─ [segmentation + blur →] encode → SignalR PushVideo (in-worker)
 *     └─ preview frame → RPC callback → Main (low frequency)
 */

// ─── Imports ────────────────────────────────────────────────────────────────

import { rpcClientServer, rpcNoWait } from 'rpc';
import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import Denque from 'denque';
import * as ort from 'onnxruntime-web';
import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { HubConnectionState } from '@microsoft/signalr';
import { encode } from '@msgpack/msgpack';
import { EventHandlerSet } from 'event-handling';

import { type EncoderConfig, type EncodedChunkData, WebCodecsEncoder } from '../webcodecs-encoder';
import type { SegmentationConfig, SegmentationStats, ModelConfig } from './segmentation-worker-contract';
import { getModelConfig } from './segmentation-worker-contract';
import type { VideoProcessingWorker, VideoProcessingWorkerCallbacks, VideoProcessingStats } from './video-processing-worker-contract';
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

// Import the ONNX model so esbuild copies it to dist/assets/onnx/
import SegmentationModelUrl from './selfie_segmentation_olive_webgpu.onnx';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoPipeline');

// ─── Section A: State ───────────────────────────────────────────────────────

// --- Encoder state ---
let encoder: WebCodecsEncoder | null = null;
let encoderConfig: EncoderConfig | null = null;
let frameCount = 0;
let resizeCanvas: OffscreenCanvas | null = null;
let resizeCtx: OffscreenCanvasRenderingContext2D | null = null;
let startTimestamp: number | undefined = undefined;
let lastLoggedFormat: string | null = '(unset)';
let loggedI420Error = false;

// Backpressure tracking
let backpressureDrops = 0;
let backpressureTotalFrames = 0;
let lastBackpressureCheckTime = 0;
const backpressureWindowMs = 5000;
const backpressureDropThreshold = 0.20;
let backpressureNotified = false;

// --- Segmentation state ---
let onnxSession: ort.InferenceSession | null = null;
let segConfig: SegmentationConfig | null = null;
let resolvedModelConfig: ModelConfig | null = null;
let outputGpuBuffer: GPUBuffer = null!;
let outputTensor: ort.Tensor = null!;
let smoothedMaskBuffer: GPUBuffer = null!;
let blurEnabled = false;
let segInitialized = false;

// Segmentation perf tracking
let segProcessedFrames = 0;
let segTotalInferenceTime = 0;
let segTotalBlurTime = 0;
let segTotalProcessingTime = 0;
let segDroppedFrames = 0;

// Frame skipping
let segFrameCounter = 0;
let hasValidMask = false;
let loggedBlurFormat = false;

// Frame queue (for segmentation async GPU processing)
interface QueuedFrame {
    frame: VideoFrame;
    sequenceNumber: number;
    timestamp: number;
}
let frameQueue: Denque<QueuedFrame> | null = null;
let processingFrame = false;
let frameSequence = 0;

// --- Streaming state (in-worker SignalR) ---
let signalrConnection: signalR.HubConnection | null = null;
let videoStream: InternalVideoStream | null = null;
let lastVideoStream: InternalVideoStream | null = null;
let pendingStreamFrames: VideoStreamFrame[] = [];
let codecSettings: string | null = null;
let storedDescriptionBytes: Uint8Array | null = null;
let firstEncodedTimestamp: number | null = null;
let sessionToken = '';
let chatId = '';
let serverClockOffsetMs = 0;
let streamingEnabled = false;

// --- Pipeline state ---
let processing = false;
let dimensionsReconciled = false;
let vadSpeaking = true;
let vadRemoteStreamCount = 0;
let vadReducedFrameIntervalMs = 1000 / 5;
let vadLastPassedFrameTime = 0;
let streamReadLoopPromise: Promise<void> | null = null;

// ─── Section B: Encoder helpers (from encoder-worker.ts) ────────────────────

function isAvcCDescription(desc: ArrayBuffer): boolean {
    if (desc.byteLength < 5) return false;
    const bytes = new Uint8Array(desc);
    if (bytes[0] !== 0x01) return false;
    const validProfiles = [66, 77, 88, 100, 110, 122, 244];
    if (!validProfiles.includes(bytes[1])) return false;
    if ((bytes[4] & 0xFC) !== 0xFC) return false;
    return true;
}

function deriveAvcCodecFromDescription(desc: ArrayBuffer): string {
    const bytes = new Uint8Array(desc);
    const profile = bytes[1].toString(16).padStart(2, '0');
    const compat = bytes[2].toString(16).padStart(2, '0');
    const level = bytes[3].toString(16).padStart(2, '0');
    return `avc1.${profile}${compat}${level}`;
}

function resizeFrame(frame: VideoFrame, targetWidth: number, targetHeight: number): VideoFrame {
    const frameWidth = frame.displayWidth;
    const frameHeight = frame.displayHeight;

    if (frameWidth === targetWidth && frameHeight === targetHeight) return frame;

    if (resizeCanvas?.width !== targetWidth || resizeCanvas.height !== targetHeight) {
        infoLog?.log(`Creating resize canvas: ${targetWidth}x${targetHeight}`);
        resizeCanvas = new OffscreenCanvas(targetWidth, targetHeight);
        resizeCtx = resizeCanvas.getContext('2d', { willReadFrequently: true });
    }

    if (!resizeCtx) {
        warnLog?.log('Could not create 2D context for resizing');
        return frame;
    }

    resizeCtx.fillStyle = '#000000';
    resizeCtx.fillRect(0, 0, targetWidth, targetHeight);

    const frameAspect = frameWidth / frameHeight;
    const targetAspect = targetWidth / targetHeight;
    let drawWidth: number, drawHeight: number, offsetX: number, offsetY: number;

    if (frameAspect > targetAspect) {
        drawWidth = targetWidth;
        drawHeight = targetWidth / frameAspect;
        offsetX = 0;
        offsetY = (targetHeight - drawHeight) / 2;
    } else {
        drawHeight = targetHeight;
        drawWidth = targetHeight * frameAspect;
        offsetX = (targetWidth - drawWidth) / 2;
        offsetY = 0;
    }

    resizeCtx.drawImage(frame, offsetX, offsetY, drawWidth, drawHeight);

    const newFrame = new VideoFrame(resizeCanvas, {
        timestamp: frame.timestamp,
        duration: frame.duration ?? undefined,
    });
    frame.close();
    return newFrame;
}

function cpuRgbaToI420(frame: VideoFrame): VideoFrame {
    const w = frame.codedWidth;
    const h = frame.codedHeight;

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

    for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
            const i = (y * w + x) * 4;
            const r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
            out[y * w + x] = Math.max(0, Math.min(255, Math.round(
                65.481 * r / 255 + 128.553 * g / 255 + 24.966 * b / 255 + 16
            )));
        }
    }

    for (let cy = 0; cy < chromaH; cy++) {
        for (let cx = 0; cx < chromaW; cx++) {
            let rSum = 0, gSum = 0, bSum = 0, count = 0;
            for (let dy = 0; dy < 2; dy++) {
                for (let dx = 0; dx < 2; dx++) {
                    const px = Math.min(cx * 2 + dx, w - 1);
                    const py = Math.min(cy * 2 + dy, h - 1);
                    const i = (py * w + px) * 4;
                    rSum += rgba[i]; gSum += rgba[i + 1]; bSum += rgba[i + 2]; count++;
                }
            }
            const r = rSum / count / 255, g = gSum / count / 255, b = bSum / count / 255;
            const chromaIdx = cy * chromaW + cx;
            out[ySize + chromaIdx] = Math.max(0, Math.min(255, Math.round(
                -37.797 * r - 74.203 * g + 112.0 * b + 128)));
            out[ySize + uvSize + chromaIdx] = Math.max(0, Math.min(255, Math.round(
                112.0 * r - 93.786 * g - 18.214 * b + 128)));
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

// ─── Section C: Segmentation helpers ────────────────────────────────────────

function initializeQueue(cfg: SegmentationConfig): void {
    frameQueue = new Denque<QueuedFrame>();
    infoLog?.log(`Segmentation frame queue initialized, maxSize=${cfg.maxQueueSize}`);
}

function enqueueFrame(frame: VideoFrame): void {
    if (!frameQueue || !segConfig) {
        // Fallback: skip segmentation, encode directly
        void encodeProcessedFrame(frame);
        return;
    }

    const queuedFrame: QueuedFrame = {
        frame,
        sequenceNumber: frameSequence++,
        timestamp: performance.now(),
    };

    // Drop oldest if queue is full
    while (frameQueue.length >= segConfig.maxQueueSize) {
        const dropped = frameQueue.shift();
        if (dropped) {
            debugLog?.log(`Dropping frame #${dropped.sequenceNumber} (queue full)`);
            dropped.frame.close();
            segDroppedFrames++;
        }
    }

    frameQueue.push(queuedFrame);

    if (!processingFrame) {
        void processQueue();
    }
}

/**
 * Send a preview frame to main thread (for own video display) and then encode.
 * Clones the frame every Nth time so the original can still go to the encoder.
 * Only called from blur processing paths — raw preview uses the RAF loop on main thread.
 */
function emitPreviewAndEncode(frame: VideoFrame): void {
    try {
        const previewFrame = new VideoFrame(frame, { timestamp: frame.timestamp });
        void callbacks.onPreviewFrame(previewFrame, rpcNoWait);
    } catch { /* clone failed, skip preview */ }
    void encodeProcessedFrame(frame);
}

/**
 * Process segmentation queue one frame at a time.
 * After blur processing, calls emitPreviewAndEncode() to send preview + encode.
 */
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
                // Reuse previous mask — skip inference but still apply blur
                const skipBlurStart = performance.now();
                if (segConfig.blurEnabled) {
                    try {
                        await submitBlurI420(
                            qf.frame, smoothedMaskBuffer,
                            segConfig.inputWidth, segConfig.inputHeight,
                            {
                                blurStrength: segConfig.blurRadius,
                                maskDirty: false,
                                outputWidth: segConfig.outputWidth,
                                outputHeight: segConfig.outputHeight,
                            },
                            (result) => {
                                if (!loggedBlurFormat) {
                                    loggedBlurFormat = true;
                                    warnLog?.log(`I420 path: GPU compute shader, frame format: ${result.frame.format}`);
                                }
                                segProcessedFrames++;
                                emitPreviewAndEncode(result.frame);
                            }
                        );
                    } catch {
                        const finalFrame = applyBackgroundBlur(
                            qf.frame, smoothedMaskBuffer,
                            segConfig.inputWidth, segConfig.inputHeight,
                            {
                                blurStrength: segConfig.blurRadius,
                                maskDirty: false,
                                outputWidth: segConfig.outputWidth,
                                outputHeight: segConfig.outputHeight,
                            }
                        );
                        segProcessedFrames++;
                        emitPreviewAndEncode(finalFrame);
                    }
                } else {
                    segProcessedFrames++;
                    void encodeProcessedFrame(qf.frame);
                }
                const skipBlurTime = performance.now() - skipBlurStart;
                segTotalBlurTime += skipBlurTime;
                segTotalProcessingTime += performance.now() - qf.timestamp;
                continue;
            }

            // Run single-frame inference
            const inferenceStartTime = performance.now();

            let inputTensor: ort.Tensor;
            if (resolvedModelConfig!.tensorFormat === 'nchw_float32') {
                inputTensor = await videoFrameToTensorFloat32(
                    qf.frame, segConfig.inputWidth, segConfig.inputHeight);
            } else {
                inputTensor = await videoFrameToTensorUint8(
                    qf.frame, segConfig.inputWidth, segConfig.inputHeight);
            }

            const outputName = onnxSession!.outputNames[0];
            const fetches: ort.InferenceSession.FetchesType = {
                [outputName]: outputTensor,
            };
            await onnxSession!.run({ [onnxSession!.inputNames[0]]: inputTensor }, fetches);

            const gpuBuffer = outputTensor.gpuBuffer;
            hasValidMask = true;
            returnPooledBuffer(inputTensor.gpuBuffer);

            const inferenceEndTime = performance.now();
            const inferenceTime = inferenceEndTime - inferenceStartTime;

            const blurStartTime = performance.now();

            if (segConfig.blurEnabled) {
                const smoothingAlpha = segConfig.temporalSmoothingFactor ?? 0.3;
                try {
                    await submitBlurI420(
                        qf.frame, smoothedMaskBuffer,
                        segConfig.inputWidth, segConfig.inputHeight,
                        {
                            blurStrength: segConfig.blurRadius,
                            smoothingSource: gpuBuffer,
                            smoothingAlpha,
                            outputWidth: segConfig.outputWidth,
                            outputHeight: segConfig.outputHeight,
                        },
                        (result) => {
                            if (!loggedBlurFormat) {
                                loggedBlurFormat = true;
                                warnLog?.log(`I420 path: GPU compute shader, frame format: ${result.frame.format}`);
                            }
                            segProcessedFrames++;
                            emitPreviewAndEncode(result.frame);
                        }
                    );
                } catch {
                    const finalFrame = applyBackgroundBlur(
                        qf.frame, smoothedMaskBuffer,
                        segConfig.inputWidth, segConfig.inputHeight,
                        {
                            blurStrength: segConfig.blurRadius,
                            smoothingSource: gpuBuffer,
                            smoothingAlpha,
                            outputWidth: segConfig.outputWidth,
                            outputHeight: segConfig.outputHeight,
                        }
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

            const blurEndTime = performance.now();
            segTotalInferenceTime += inferenceTime;
            segTotalBlurTime += blurEndTime - blurStartTime;
            segTotalProcessingTime += performance.now() - qf.timestamp;
        }
    } finally {
        processingFrame = false;
    }
}

/**
 * Initialize ONNX Runtime + WebGPU for segmentation.
 * Can be called lazily (on first toggleBlur(true)) or eagerly.
 */
async function initializeSegmentation(config: SegmentationConfig): Promise<void> {
    segConfig = config;

    ort.env.wasm.wasmPaths = 'https://cdn.jsdelivr.net/npm/onnxruntime-web@1.23.2/dist/';
    ort.env.wasm.numThreads = 1;

    const modelUrl = SegmentationModelUrl;

    const sessionOptions: ort.InferenceSession.SessionOptions = {
        executionProviders: [{ name: 'webgpu', preferredLayout: 'NCHW' }],
        graphOptimizationLevel: 'all',
        executionMode: 'parallel',
        enableCpuMemArena: true,
        enableMemPattern: true,
        preferredOutputLocation: 'gpu-buffer',
        enableGraphCapture: true,
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
    const outputBufferSize = maskSize * 4;
    const gpuDevice = WebGPUManager.get();
    outputGpuBuffer = gpuDevice.createBuffer({
        size: outputBufferSize,
        usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST,
    });
    smoothedMaskBuffer = gpuDevice.createBuffer({
        size: outputBufferSize,
        usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST,
    });

    outputTensor = ort.Tensor.fromGpuBuffer(outputGpuBuffer, {
        dataType: 'float32',
        dims: [1, 1, config.inputHeight, config.inputWidth],
        dispose: () => { /* managed by worker */ },
    });

    resolvedModelConfig = config.modelConfig ?? getModelConfig(modelUrl);
    infoLog?.log(`Model config: format=${resolvedModelConfig.tensorFormat}`);

    initializeQueue(config);
    segInitialized = true;
}

// ─── Section D: In-worker SignalR streaming ─────────────────────────────────

interface VideoStreamFrame {
    offset: number;
    duration: number;
    isKeyFrame: boolean;
    width: number;
    height: number;
    data: Uint8Array;
    description?: Uint8Array;
    codec?: string;
}

function microsecondsToTicks(microseconds: number): number {
    return microseconds * 10;
}

function encodeStreamFrame(frame: VideoStreamFrame): Uint8Array {
    const obj: Record<string, unknown> = {
        offset: frame.offset,
        duration: frame.duration,
        data: frame.data,
    };
    if (frame.isKeyFrame) {
        obj.isKeyFrame = true;
        obj.width = frame.width;
        obj.height = frame.height;
    }
    if (frame.description) obj.description = frame.description;
    if (frame.codec) obj.codec = frame.codec;
    return encode(obj);
}

function serverClockNow(): number {
    return Date.now() + serverClockOffsetMs;
}

/**
 * Internal VideoStream — simplified version that lives in the worker.
 */
class InternalVideoStream {
    private readonly frames = new Denque<VideoStreamFrame>();
    private readonly frameAdded = new EventHandlerSet<void>();
    private addedFrameCount = 0;

    public isCompleted = false;
    public isDisposed = false;
    public readonly whenDisposed: Promise<void>;

    constructor(
        private readonly config: { codec: string; width: number; height: number; codecSettings: string },
        private readonly onReconnect?: () => void,
        streamAfter?: Promise<void>,
    ) {
        this.whenDisposed = this.stream(streamAfter);
    }

    addFrame(frame: VideoStreamFrame): void {
        if (this.isCompleted) return;
        if (frame.data.byteLength === 0) return;

        this.frames.push(frame);
        this.addedFrameCount++;

        if (this.addedFrameCount <= 3 || this.addedFrameCount % 300 === 0) {
            debugLog?.log(`addFrame: total: ${this.addedFrameCount} queue: ${this.frames.length} ` +
                `isKey: ${frame.isKeyFrame} size: ${frame.data.byteLength}`);
        }

        this.frameAdded.trigger();
    }

    complete(): void {
        this.isCompleted = true;
        this.frameAdded.trigger();
    }

    private async stream(streamAfter?: Promise<void>): Promise<void> {
        try {
            if (streamAfter) await streamAfter;

            const subject = new signalR.Subject<Uint8Array>();

            // Ensure connected
            while (signalrConnection?.state !== HubConnectionState.Connected && processing) {
                if (signalrConnection?.state === HubConnectionState.Disconnected) {
                    try { await signalrConnection.start(); } catch { /* retry */ }
                }
                await new Promise(r => setTimeout(r, 100));
            }
            if (!processing) return;

            this.onReconnect?.();

            const clientStartOffset = serverClockNow() / 1000;
            warnLog?.log(`TIMING_ANCHOR: clientStartOffset=${clientStartOffset.toFixed(3)}s`);

            infoLog?.log(`PushVideo: codec=${this.config.codec}, ` +
                `${this.config.width}x${this.config.height}, settings=${this.config.codecSettings.length} chars`);

            void signalrConnection!.send('PushVideo',
                sessionToken, chatId,
                this.config.codec, this.config.width, this.config.height,
                this.config.codecSettings, clientStartOffset, subject);

            // Frame pump loop
            while (!this.isCompleted || !this.frames.isEmpty()) {
                while (!this.frames.isEmpty()) {
                    const frame = this.frames.shift()!;
                    const encoded = encodeStreamFrame(frame);
                    subject.next(encoded);
                }
                if (!this.isCompleted) {
                    await this.frameAdded.whenNextVoid();
                }
            }

            subject.complete();
        } catch (error) {
            errorLog?.log('VideoStream error:', error);
        } finally {
            this.isDisposed = true;
        }
    }
}

function initSignalR(hubUrl: string): void {
    if (signalrConnection) {
        infoLog?.log('SignalR connection already exists');
        return;
    }

    signalrConnection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
            transport: signalR.HttpTransportType.WebSockets,
            skipNegotiation: true,
        })
        .withHubProtocol(new MessagePackHubProtocol())
        .withAutomaticReconnect()
        .build();

    signalrConnection.onclose(() => warnLog?.log('SignalR connection closed'));
    signalrConnection.onreconnecting(() => warnLog?.log('SignalR reconnecting...'));
    signalrConnection.onreconnected(() => infoLog?.log('SignalR reconnected'));

    void signalrConnection.start().then(() => {
        infoLog?.log('SignalR connected');
    }).catch((error: unknown) => {
        errorLog?.log('SignalR connection failed:', error);
    });
}

// ─── Section E: Frame processing pipeline ───────────────────────────────────

/**
 * Process one frame: segmentation (if enabled) → encode.
 * Called from stream read loop or RPC encodeFrame().
 */
function processOneFrame(frame: VideoFrame): void {
    if (!encoder || !processing || !encoderConfig) {
        frame.close();
        return;
    }

    // Backpressure check
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
            backpressureDrops = 0;
            backpressureTotalFrames = 0;
            lastBackpressureCheckTime = now;
        }
        return;
    }

    // Reset backpressure flag after sustained success
    if (backpressureNotified && backpressureTotalFrames > 30) {
        const now = performance.now();
        if (now - lastBackpressureCheckTime > backpressureWindowMs) {
            const dropRate = backpressureDrops / backpressureTotalFrames;
            if (dropRate < 0.05) backpressureNotified = false;
            backpressureDrops = 0;
            backpressureTotalFrames = 0;
            lastBackpressureCheckTime = now;
        }
    }

    if (blurEnabled && segInitialized) {
        // Route through segmentation queue → processQueue → encodeProcessedFrame
        enqueueFrame(frame);
    } else {
        // Direct encode (no blur)
        void encodeProcessedFrame(frame);
    }
}

/**
 * Encode a processed frame (after blur or directly).
 * Handles resize, YUV conversion, timestamp normalization, and encoding.
 */
async function encodeProcessedFrame(frame: VideoFrame): Promise<void> {
    if (!encoder || !processing || !encoderConfig) {
        frame.close();
        return;
    }

    try {
        if (startTimestamp === undefined) {
            startTimestamp = frame.timestamp;
            infoLog?.log(`Start timestamp set to ${startTimestamp}μs`);
        }

        // Resize if needed
        let processedFrame = resizeFrame(frame, encoderConfig.width, encoderConfig.height);

        // YUV conversion (mobile hardware encoders expect NV12/I420)
        const format = processedFrame.format as string | null;
        const isAlreadyYuv = format === 'NV12' || format === 'I420'
            || format === 'I420A' || format === 'I422' || format === 'I444'
            || format === 'NV12A';

        if (!isAlreadyYuv) {
            let converted = false;
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

        if (processedFrame.format !== lastLoggedFormat) {
            lastLoggedFormat = processedFrame.format;
            warnLog?.log(`Frame format: ${processedFrame.format}, ${processedFrame.codedWidth}x${processedFrame.codedHeight}`);
        }

        // Timestamp normalization
        const normalizedTs = processedFrame.timestamp - startTimestamp;
        if (normalizedTs !== processedFrame.timestamp) {
            const normalized = new VideoFrame(processedFrame, {
                timestamp: normalizedTs,
                duration: processedFrame.duration ?? undefined,
            });
            processedFrame.close();
            processedFrame = normalized;
        }

        const isKeyFrame = frameCount % 30 === 0;
        encoder.encode(processedFrame, isKeyFrame);
        frameCount++;
    } catch (error) {
        errorLog?.log('Error encoding frame:', error);
        try { frame.close(); } catch { /* already closed */ }
    }
}

/**
 * WebCodecs encoder output callback.
 * Serializes chunk and either streams to SignalR (in-worker) or sends via RPC.
 */
function onEncoderOutput(chunkData: EncodedChunkData): void {
    // 1. Copy chunk bytes
    const chunkBuffer = new ArrayBuffer(chunkData.byteLength);
    chunkData.chunk.copyTo(new Uint8Array(chunkBuffer));

    // 2. Validate codec + extract description
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

        if (isAvcCDescription(descBuffer) && !encoderConfig!.codec.startsWith('avc1')) {
            const derivedCodec = deriveAvcCodecFromDescription(descBuffer);
            warnLog?.log(`Encoder output mismatch: configured=${encoderConfig!.codec} but output is avcC, correcting to ${derivedCodec}`);
            actualCodec = derivedCodec;
            encoderConfig!.codec = derivedCodec;
        }
    }

    // 3. Route to SignalR (in-worker) or RPC fallback
    if (streamingEnabled) {
        deliverChunkToStream(chunkBuffer, chunkData.chunk.timestamp, chunkData.chunk.duration ?? 0,
            chunkData.type === 'key', actualCodec, chunkData.sequenceNumber, descBuffer);
    } else {
        // RPC fallback
        void callbacks.onSerializedChunk(
            chunkBuffer, chunkData.chunk.timestamp, chunkData.chunk.duration ?? 0,
            chunkData.type === 'key', actualCodec, chunkData.sequenceNumber, descBuffer, rpcNoWait);
    }
}

/**
 * Deliver encoded chunk to in-worker SignalR stream.
 * Moved from video-pipeline.ts onSerializedChunk.
 */
function deliverChunkToStream(
    chunkBytes: ArrayBuffer, timestamp: number, duration: number,
    isKeyFrame: boolean, codec: string, sequenceNumber: number,
    descriptionBytes?: ArrayBuffer
): void {
    const chunkData = new Uint8Array(chunkBytes);
    firstEncodedTimestamp ??= timestamp;
    const normalizedTimestamp = timestamp - firstEncodedTimestamp;

    const frame: VideoStreamFrame = {
        offset: microsecondsToTicks(normalizedTimestamp),
        duration: microsecondsToTicks(duration),
        isKeyFrame,
        width: encoderConfig!.width,
        height: encoderConfig!.height,
        data: chunkData,
        codec: isKeyFrame ? codec : undefined,
    };

    if (isKeyFrame) {
        const offsetMs = normalizedTimestamp / 1000;
        debugLog?.log(`Streaming keyframe: seq=${sequenceNumber}, offsetMs=${offsetMs.toFixed(0)}, ${(chunkData.length / 1024).toFixed(2)} KB`);
    }

    // Extract/store codec description
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

    // Create or feed VideoStream
    if (!videoStream) {
        const isAV1 = encoderConfig!.codec.startsWith('av01');
        const canCreateStream = codecSettings ?? (isAV1 && isKeyFrame);

        if (canCreateStream) {
            const settings = codecSettings ?? '';
            infoLog?.log(`Creating VideoStream with codecSettings (${settings.length} chars)`);
            videoStream = new InternalVideoStream(
                { codec: encoderConfig!.codec, width: encoderConfig!.width, height: encoderConfig!.height, codecSettings: settings },
                () => { firstEncodedTimestamp = null; },
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
        errorLog?.log('Encoder error:', error);
    });
    encoder.initialize();
}

// ─── Section F: Stream read loop ────────────────────────────────────────────

async function streamReadLoop(inputReader: ReadableStreamDefaultReader<VideoFrame>): Promise<void> {
    try {
        while (processing) {
            const { done, value: rawFrame } = await inputReader.read();
            if (done) {
                infoLog?.log('Stream input ended');
                break;
            }

            if (!encoder || !processing || !encoderConfig) { // eslint-disable-line @typescript-eslint/no-unnecessary-condition
                rawFrame.close();
                continue;
            }

            // Dimension reconciliation on first frame
            if (!dimensionsReconciled) {
                dimensionsReconciled = true;
                const frameW = rawFrame.codedWidth;
                const frameH = rawFrame.codedHeight;
                if (frameW !== encoderConfig.width || frameH !== encoderConfig.height) {
                    warnLog?.log(`Frame dimensions ${frameW}x${frameH} differ from config ${encoderConfig.width}x${encoderConfig.height}, reconfiguring`);
                    encoderConfig.width = frameW;
                    encoderConfig.height = frameH;
                    await encoder.reconfigure({ width: frameW, height: frameH, bitrate: encoderConfig.bitrate });
                    if (segConfig) {
                        segConfig.outputWidth = frameW;
                        segConfig.outputHeight = frameH;
                    }
                    void callbacks.onDimensionReconciled(frameW, frameH, rpcNoWait);
                }
            }

            // Normalize display dimensions (Android MSTP issue)
            let frame = rawFrame;
            if (rawFrame.displayWidth !== rawFrame.codedWidth || rawFrame.displayHeight !== rawFrame.codedHeight) {
                frame = new VideoFrame(rawFrame, {
                    displayWidth: rawFrame.codedWidth,
                    displayHeight: rawFrame.codedHeight,
                });
                rawFrame.close();
            }

            // VAD-based adaptive framerate
            if (!vadSpeaking && vadRemoteStreamCount >= 2) {
                const now = performance.now();
                if (now - vadLastPassedFrameTime < vadReducedFrameIntervalMs) {
                    frame.close();
                    continue;
                }
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

// ─── Section G: serverImpl ──────────────────────────────────────────────────

const serverImpl: VideoProcessingWorker = {
     
    startWithStream: async (config, frameInputStream): Promise<void> => {
        try {
            infoLog?.log('Starting video processing worker (stream mode)...');

            // Store streaming config
            sessionToken = config.streaming.sessionToken;
            chatId = config.streaming.chatId;
            serverClockOffsetMs = config.streaming.serverClockOffsetMs;
            streamingEnabled = true;

            // Init SignalR
            initSignalR(config.streaming.hubUrl);

            // Init segmentation if configured
            if (config.segmentation) {
                const segCfg = {
                    ...config.segmentation,
                    outputWidth: config.encoder.width,
                    outputHeight: config.encoder.height,
                };
                await initializeSegmentation(segCfg);
                blurEnabled = true;
                infoLog?.log('Segmentation initialized (blur enabled)');
            }

            // Init encoder
            createEncoder(config.encoder);

            // VAD
            if (config.adaptiveFramerate) {
                vadReducedFrameIntervalMs = 1000 / config.adaptiveFramerate.reducedFps;
            }

            processing = true;
            frameCount = 0;
            dimensionsReconciled = false;

            // Start reading from MSTP stream
            const inputReader = frameInputStream.getReader();
            streamReadLoopPromise = streamReadLoop(inputReader);

            infoLog?.log('Video processing worker started (stream mode)');
        } catch (error) {
            errorLog?.log('Failed to start stream mode:', error);
            throw error;
        }
    },

     
    initialize: async (config): Promise<void> => {
        try {
            infoLog?.log('Starting video processing worker (RPC mode)...');

            sessionToken = config.streaming.sessionToken;
            chatId = config.streaming.chatId;
            serverClockOffsetMs = config.streaming.serverClockOffsetMs;
            streamingEnabled = true;

            initSignalR(config.streaming.hubUrl);

            if (config.segmentation) {
                const segCfg = {
                    ...config.segmentation,
                    outputWidth: config.encoder.width,
                    outputHeight: config.encoder.height,
                };
                await initializeSegmentation(segCfg);
                blurEnabled = true;
            }

            createEncoder(config.encoder);

            if (config.adaptiveFramerate) {
                vadReducedFrameIntervalMs = 1000 / config.adaptiveFramerate.reducedFps;
            }

            processing = true;
            frameCount = 0;

            infoLog?.log('Video processing worker started (RPC mode)');
        } catch (error) {
            errorLog?.log('Failed to initialize:', error);
            throw error;
        }
    },

    // eslint-disable-next-line
    encodeFrame: async (frame): Promise<void> => {
        processOneFrame(frame);
    },

    // eslint-disable-next-line
    setVadState: async (speaking, remoteStreamCount): Promise<void> => {
        vadSpeaking = speaking;
        vadRemoteStreamCount = remoteStreamCount;
        if (speaking) vadLastPassedFrameTime = 0;
        debugLog?.log(`VAD state: speaking=${speaking}, remoteStreamCount=${remoteStreamCount}`);
    },

     
    reconfigure: async (params): Promise<void> => {
        if (!encoder || !processing || !encoderConfig) {
            warnLog?.log('Cannot reconfigure: not active');
            return;
        }

        infoLog?.log(`Reconfigure: ${params.bitrate / 1_000_000}Mbps, ${params.width}x${params.height}`);

        encoderConfig.bitrate = params.bitrate;
        encoderConfig.width = params.width;
        encoderConfig.height = params.height;

        await encoder.reconfigure(params);

        resizeCanvas = null;
        resizeCtx = null;

        if (segConfig && blurEnabled) {
            segConfig.outputWidth = params.width;
            segConfig.outputHeight = params.height;
        }
    },

    switchCodec: async (config: EncoderConfig): Promise<void> => {
        if (!encoder) {
            warnLog?.log('Cannot switch codec: not active');
            return;
        }

        infoLog?.log(`Switching codec to ${config.codec}`);

        // Complete current stream
        if (videoStream) {
            videoStream.complete();
            videoStream = null;
        }

        codecSettings = null;
        firstEncodedTimestamp = null;
        pendingStreamFrames = [];
        storedDescriptionBytes = null;

        await encoder.switchCodec(config);
        encoderConfig = config;
        resizeCanvas = null;
        resizeCtx = null;
        frameCount = 0;
        startTimestamp = undefined;
        backpressureDrops = 0;
        backpressureTotalFrames = 0;
        lastBackpressureCheckTime = 0;
        backpressureNotified = false;

        infoLog?.log('Codec switched successfully');
    },

     
    toggleBlur: async (enabled, segCfg?): Promise<void> => {
        infoLog?.log(`Toggling blur: ${enabled ? 'ON' : 'OFF'}`);

        if (enabled && !segInitialized) {
            if (!segCfg && !segConfig) {
                throw new Error('Cannot enable blur: no segmentation config');
            }
            const cfg = segCfg ?? segConfig!;
            const fullCfg = {
                ...cfg,
                outputWidth: encoderConfig?.width ?? cfg.outputWidth,
                outputHeight: encoderConfig?.height ?? cfg.outputHeight,
            };
            await initializeSegmentation(fullCfg);
        }

        if (segConfig) {
            segConfig.blurEnabled = enabled;
        }
        blurEnabled = enabled;
    },

    // eslint-disable-next-line
    forceKeyFrame: async (): Promise<void> => {
        frameCount = 0;
        infoLog?.log('Forced next frame to be keyframe');
    },

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

    stop: async (): Promise<void> => {
        infoLog?.log('Stopping video processing worker...');

        processing = false;

        // Wait for stream read loop
        if (streamReadLoopPromise) {
            try { await streamReadLoopPromise; } catch { /* ignore */ }
            streamReadLoopPromise = null;
        }

        // Wait for segmentation readbacks
        try { await awaitAllPendingReadbacks(); } catch { /* ignore */ }

        // Clear frame queue
        if (frameQueue) {
            while (!frameQueue.isEmpty()) {
                const qf = frameQueue.shift();
                if (qf) qf.frame.close();
            }
            frameQueue = null;
        }

        // Flush and close encoder
        if (encoder) {
            try {
                await encoder.flush();
                encoder.close();
            } catch (error) {
                warnLog?.log('Encoder close error:', error);
            }
        }

        // Complete video stream
        if (videoStream) {
            videoStream.complete();
            videoStream = null;
        }

        // Close SignalR
        if (signalrConnection) {
            try { await signalrConnection.stop(); } catch { /* ignore */ }
            signalrConnection = null;
        }

        // Destroy GPU buffers
        if (segInitialized) {
            try { outputGpuBuffer.destroy(); } catch { /* ignore */ }
            try { smoothedMaskBuffer.destroy(); } catch { /* ignore */ }
        }

        // Reset state
        encoder = null;
        encoderConfig = null;
        onnxSession = null;
        segConfig = null;
        resolvedModelConfig = null;
        segInitialized = false;
        blurEnabled = false;
        resizeCanvas = null;
        resizeCtx = null;
        frameCount = 0;
        startTimestamp = undefined;
        lastLoggedFormat = '(unset)';
        loggedI420Error = false;
        backpressureDrops = 0;
        backpressureTotalFrames = 0;
        lastBackpressureCheckTime = 0;
        backpressureNotified = false;
        dimensionsReconciled = false;
        vadSpeaking = true;
        vadRemoteStreamCount = 0;
        vadLastPassedFrameTime = 0;
        segFrameCounter = 0;
        hasValidMask = false;
        loggedBlurFormat = false;
        processingFrame = false;
        frameSequence = 0;
        segProcessedFrames = 0;
        segTotalInferenceTime = 0;
        segTotalBlurTime = 0;
        segTotalProcessingTime = 0;
        segDroppedFrames = 0;
        videoStream = null;
        lastVideoStream = null;
        pendingStreamFrames = [];
        codecSettings = null;
        storedDescriptionBytes = null;
        firstEncodedTimestamp = null;
        streamingEnabled = false;

        infoLog?.log('Video processing worker stopped');
    },

    // eslint-disable-next-line
    getStats: async (): Promise<VideoProcessingStats> => {
        const encoderStats = encoder?.getStats() ?? {
            encodedFrames: 0, droppedFrames: 0, keyFrames: 0, totalBytes: 0,
            averageEncodeTime: 0, medianEncodeTime: 0, pureMedianEncodeTime: -1,
            configuredWidth: 0, configuredHeight: 0, configuredBitrate: 0,
            hardwareAcceleration: 'unknown',
        };

        const segStats: SegmentationStats | null = segInitialized ? {
            processedFrames: segProcessedFrames,
            averageInferenceTime: segProcessedFrames > 0 ? segTotalInferenceTime / segProcessedFrames : 0,
            averageBlurTime: segProcessedFrames > 0 ? segTotalBlurTime / segProcessedFrames : 0,
            averageTotalTime: segProcessedFrames > 0 ? segTotalProcessingTime / segProcessedFrames : 0,
            droppedFrames: segDroppedFrames,
            backend: segConfig?.backend ?? 'unknown',
        } : null;

        return { encoder: encoderStats, segmentation: segStats };
    },

    // eslint-disable-next-line
    updateSessionToken: async (token): Promise<void> => {
        sessionToken = token;
    },

    // eslint-disable-next-line
    updateServerClockOffset: async (offsetMs): Promise<void> => {
        serverClockOffsetMs = offsetMs;
    },
};

// ─── Section H: RPC initialization ─────────────────────────────────────────

const callbacks = rpcClientServer<VideoProcessingWorkerCallbacks>(
    'VideoProcessingWorker',
    self as unknown as Worker,
    serverImpl,
);

infoLog?.log('Video processing worker initialized');
