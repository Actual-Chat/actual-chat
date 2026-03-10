/**
 * Segmentation Worker (Universal - Chrome & Safari)
 * Handles background blur segmentation using ONNX Runtime Web with WebGPU
 * Uses GPU buffer-backed tensors for zero-copy inference
 * Processes frames one-by-one (no batching) for lowest latency
 */

import Denque from 'denque';
import { rpcClientServer, RpcNoWait } from 'rpc';
import * as ort from 'onnxruntime-web';
import {
    initTensorWebGPU,
    processDeferredCleanups,
    returnPooledBuffer,
    videoFrameToTensorFloat32,
    videoFrameToTensorUint8,
} from '../tensor-utils';
import { applyBackgroundBlur, submitBlurI420, awaitAllPendingReadbacks, applyTemporalSmoothing, initBlurWebGPU, processBlurDeferredCleanups } from '../webgpu-blur';
import { WebGPUManager } from '../webgpu-manager';

import type {
    ModelConfig,
    SegmentationConfig,
    SegmentationStats,
    SegmentationWorker,
    SegmentationWorkerCallbacks,
} from './segmentation-worker-contract';
import { DEFAULT_MODEL_CONFIG, getModelConfig } from './segmentation-worker-contract';
import { Log } from 'logging';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoSegmentation');

// Import the ONNX model so esbuild copies it to dist/assets/onnx/
import SegmentationModelUrl from './selfie_segmentation_olive_webgpu.onnx';

// Worker state
let session: ort.InferenceSession | null = null;
let config: SegmentationConfig | null = null;
let processing = false;
let resolvedModelConfig: ModelConfig | null = null; // Cached model config for tensor conversion
let outputGpuBuffer: GPUBuffer = null!; // Reusable output GPU buffer
let outputTensor: ort.Tensor = null!; // Reusable output tensor
let smoothedMaskBuffer: GPUBuffer = null!; // Temporally smoothed mask buffer

// Performance tracking
let processedFrames = 0;
let totalInferenceTime = 0;
let totalBlurTime = 0;
let totalProcessingTime = 0;
let droppedFrames = 0;

// Frame skipping state
let frameCounter = 0;
let hasValidMask = false; // Whether outputGpuBuffer contains a valid mask from a previous inference
let loggedFormat = false; // One-time diagnostic: log the actual frame format

// Frame queuing state (for non-blocking async processing)
interface QueuedFrame {
  frame: VideoFrame;
  sequenceNumber: number;
  timestamp: number;
}

let frameQueue: Denque<QueuedFrame> | null = null;
let processingFrame = false; // Whether we're currently processing a frame
let frameSequence = 0;


/**
 * Initialize frame queue for async non-blocking processing
 */
function initializeQueue(cfg: SegmentationConfig) {
    frameQueue = new Denque<QueuedFrame>();
    infoLog?.log(`Frame queue initialized, maxSize=${cfg.maxQueueSize}`);
}

/**
 * Enqueue a frame for processing
 * Triggers immediate processing if not already processing
 */
function enqueueFrame(frame: VideoFrame): void {
    if (!frameQueue || !config) {
    // Fallback: return original frame if queue not initialized
        pipeline.onFrameProcessed(frame, frameSequence++, 0);
        return;
    }

    // Create queued frame object
    const queuedFrame: QueuedFrame = {
        frame: frame,
        sequenceNumber: frameSequence++,
        timestamp: performance.now()
    };

    // Drop oldest if queue is full
    while (frameQueue.length >= config.maxQueueSize) {
        const dropped = frameQueue.shift();
        if (dropped) {
            debugLog?.log(`Dropping frame #${dropped.sequenceNumber} (queue full)`);
            dropped.frame.close();
            droppedFrames++;
        }
    }

    frameQueue.push(queuedFrame);

    // Trigger processing if not already running
    if (!processingFrame) {
        void processQueue();
    }
}


/**
 * Process frames from queue one at a time
 * Single-frame inference for lowest latency - no batching
 */
async function processQueue(): Promise<void> {
    if (!frameQueue || !config || processingFrame) {
        return;
    }

    processingFrame = true;

    try {
        while (!frameQueue.isEmpty() && processing) {
            const qf = frameQueue.shift();
            if (!qf) break;
            frameCounter++;

            // Process deferred cleanups from previous frames (no sync overhead)
            processDeferredCleanups();
            processBlurDeferredCleanups();

            // Add frame skipping to reduce GPU load and allow video decoding to catch up
            // Skip every N frames to reduce contention, especially on mobile devices
            // On skipped frames, reuse the previous mask to still produce blurred output
            const frameSkipInterval = config.frameSkipInterval ?? 1;
            if (frameSkipInterval > 1 && frameCounter % frameSkipInterval !== 0 && hasValidMask) {
                // Reuse previous smoothed mask - skip inference but still apply blur
                const skipBlurStart = performance.now();
                if (config.blurEnabled) {
                    try {
                        // Fire-and-forget GPU I420 path: blur + RGBA→I420, callback on readback
                        const seqNum = qf.sequenceNumber;
                        await submitBlurI420(
                            qf.frame,
                            smoothedMaskBuffer,
                            config.inputWidth,
                            config.inputHeight,
                            {
                                blurStrength: config.blurRadius,
                                maskDirty: false,
                                outputWidth: config.outputWidth,
                                outputHeight: config.outputHeight,
                            },
                            (result) => {
                                if (!loggedFormat) {
                                    loggedFormat = true;
                                    warnLog?.log(`I420 path: GPU compute shader, frame format: ${result.frame.format}`);
                                }
                                processedFrames++;
                                pipeline.onFrameProcessed(result.frame, seqNum, 0);
                            }
                        );
                    } catch {
                        // Fallback: render to canvas (RGBA) if GPU I420 fails
                        const finalFrame = applyBackgroundBlur(
                            qf.frame,
                            smoothedMaskBuffer,
                            config.inputWidth,
                            config.inputHeight,
                            {
                                blurStrength: config.blurRadius,
                                maskDirty: false,
                                outputWidth: config.outputWidth,
                                outputHeight: config.outputHeight,
                            }
                        );
                        if (!loggedFormat) {
                            loggedFormat = true;
                            warnLog?.log(`I420 path: RGBA fallback (GPU compute failed), frame format: ${finalFrame.format}`);
                        }
                        processedFrames++;
                        pipeline.onFrameProcessed(finalFrame, qf.sequenceNumber, performance.now() - skipBlurStart);
                    }
                } else {
                    processedFrames++;
                    pipeline.onFrameProcessed(qf.frame, qf.sequenceNumber, 0);
                }

                const skipBlurTime = performance.now() - skipBlurStart;
                totalBlurTime += skipBlurTime;
                totalProcessingTime += performance.now() - qf.timestamp;

                continue;
            }

            // Run single-frame inference
            const inferenceStartTime = performance.now();

            // Create input tensor based on model format
            let inputTensor: ort.Tensor;
            if (resolvedModelConfig!.tensorFormat === 'nchw_float32') {
                inputTensor = await videoFrameToTensorFloat32(
                    qf.frame,
                    config.inputWidth,
                    config.inputHeight
                );
            } else {
                // Default: NHWC uint8 format (backward compatible)
                inputTensor = await videoFrameToTensorUint8(qf.frame, config.inputWidth, config.inputHeight);
            }

            // Get output format configuration
            const outputFormat = resolvedModelConfig!.outputFormat ?? DEFAULT_MODEL_CONFIG.outputFormat;
            const outputLayout = resolvedModelConfig!.outputLayout ?? DEFAULT_MODEL_CONFIG.outputLayout;

            // Run inference with GPU buffer tensor - output will also be on GPU
            const outputName = session!.outputNames[0];

            // Use fetches to specify the output tensor for reuse
            const fetches: ort.InferenceSession.FetchesType = {
                [outputName]: outputTensor
            };

            await session!.run({ [session!.inputNames[0]]: inputTensor }, fetches);

            // Validate output tensor dimensions match expected layout
            const dims = outputTensor.dims;
            if (outputFormat === 'single_channel' && dims.length === 4) {
                const expectedNHWC = dims[3] === 1 && dims[1] === config.inputHeight && dims[2] === config.inputWidth;
                const expectedNCHW = dims[1] === 1 && dims[2] === config.inputHeight && dims[3] === config.inputWidth;

                if (outputLayout === 'nhwc' && !expectedNHWC && expectedNCHW) {
                    warnLog?.log(`Output layout mismatch: config says 'nhwc' [1,H,W,1] but tensor is [${dims.join(',')}] which looks like NCHW [1,1,H,W]`);
                } else if (outputLayout === 'nchw' && !expectedNCHW && expectedNHWC) {
                    warnLog?.log(`Output layout mismatch: config says 'nchw' [1,1,H,W] but tensor is [${dims.join(',')}] which looks like NHWC [1,H,W,1]`);
                }
            }

            const gpuBuffer = outputTensor.gpuBuffer;

            // Single channel - use GPU buffer directly - no splitting needed for single frame!
            hasValidMask = true;

            // Return input tensor's GPU buffer to pool for reuse
            returnPooledBuffer(inputTensor.gpuBuffer);

            const inferenceEndTime = performance.now();
            const inferenceTime = inferenceEndTime - inferenceStartTime;

            // Time the blur operation
            const blurStartTime = performance.now();

            if (config.blurEnabled) {
                // Apply background blur with merged temporal smoothing + GPU I420 conversion
                const smoothingAlpha = config.temporalSmoothingFactor ?? 0.3;
                const seqNum = qf.sequenceNumber;
                try {
                    // Fire-and-forget GPU I420 path: blur + temporal smoothing + RGBA→I420
                    await submitBlurI420(
                        qf.frame,
                        smoothedMaskBuffer,
                        config.inputWidth,
                        config.inputHeight,
                        {
                            blurStrength: config.blurRadius,
                            smoothingSource: gpuBuffer,
                            smoothingAlpha,
                            outputWidth: config.outputWidth,
                            outputHeight: config.outputHeight,
                        },
                        (result) => {
                            if (!loggedFormat) {
                                loggedFormat = true;
                                warnLog?.log(`I420 path: GPU compute shader, frame format: ${result.frame.format}`);
                            }
                            processedFrames++;
                            pipeline.onFrameProcessed(result.frame, seqNum, 0);
                        }
                    );
                } catch {
                    // Fallback: render to canvas (RGBA) if GPU I420 fails
                    const finalFrame = applyBackgroundBlur(
                        qf.frame,
                        smoothedMaskBuffer,
                        config.inputWidth,
                        config.inputHeight,
                        {
                            blurStrength: config.blurRadius,
                            smoothingSource: gpuBuffer,
                            smoothingAlpha,
                            outputWidth: config.outputWidth,
                            outputHeight: config.outputHeight,
                        }
                    );
                    if (!loggedFormat) {
                        loggedFormat = true;
                        warnLog?.log(`I420 path: RGBA fallback (GPU compute failed), frame format: ${finalFrame.format}`);
                    }
                    processedFrames++;
                    pipeline.onFrameProcessed(finalFrame, seqNum, performance.now() - qf.timestamp);
                }
            } else {
                // No blur — still need temporal smoothing as standalone
                const smoothingAlpha = config.temporalSmoothingFactor ?? 0.3;
                const maskSize = config.inputWidth * config.inputHeight;
                applyTemporalSmoothing(gpuBuffer, smoothedMaskBuffer, maskSize, smoothingAlpha);
                processedFrames++;
                pipeline.onFrameProcessed(qf.frame, qf.sequenceNumber, performance.now() - qf.timestamp);
            }

            const blurEndTime = performance.now();
            const blurTime = blurEndTime - blurStartTime;

            // Update performance tracking (CPU-side times only)
            totalInferenceTime += inferenceTime;
            totalBlurTime += blurTime;
            totalProcessingTime += performance.now() - qf.timestamp;
        }

    } finally {
        processingFrame = false;
    }
}


const serverImpl: SegmentationWorker = {
    /**
   * Initialize the segmentation worker with WebGPU support
   */
    initialize: async (segmentationConfig: SegmentationConfig): Promise<void> => {
        try {
            infoLog?.log('Initializing segmentation worker...');

            config = segmentationConfig;

            // Specify WASM paths for ONNX Runtime to allow loading WASM backend
            ort.env.wasm.wasmPaths = 'https://cdn.jsdelivr.net/npm/onnxruntime-web@1.23.2/dist/';
            ort.env.wasm.numThreads = 1; // Use single thread for WASM backend

            // Use the bundled model URL (resolved by esbuild import)
            const modelUrl = SegmentationModelUrl;

            try {
                // Configure session options for WebGPU (only supported backend)
                const sessionOptions: ort.InferenceSession.SessionOptions = {
                    executionProviders: [{
                        name: 'webgpu',
                        preferredLayout: 'NCHW',
                    }],
                    graphOptimizationLevel: 'all',
                    executionMode: 'parallel',
                    enableCpuMemArena: true,
                    enableMemPattern: true,
                    preferredOutputLocation: 'gpu-buffer',
                    enableGraphCapture: true
                };

                infoLog?.log('Loading model from:', modelUrl);

                session = await ort.InferenceSession.create(modelUrl, sessionOptions);

                infoLog?.log('Model loaded with WebGPU backend');

                // Get ORT's WebGPU device for shared usage
                const blurDevice = await ort.env.webgpu.device;
                infoLog?.log('Using shared WebGPU device');
                // Initialize shared WebGPU manager with the blur device
                await WebGPUManager.init(blurDevice);

                // Initialize tensor utils
                await initTensorWebGPU(blurDevice);

                // Initialize blur module with WebGPU device
                await initBlurWebGPU(blurDevice);
                infoLog?.log('WebGPU resources initialized');

                // Pre-allocate output GPU buffer to enable reuse
                const maskSize = config.inputWidth * config.inputHeight;
                const outputBufferSize = maskSize * 4; // float32 size
                const gpuDevice = WebGPUManager.get();
                outputGpuBuffer = gpuDevice.createBuffer({
                    size: outputBufferSize,
                    usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST
                });
                smoothedMaskBuffer = gpuDevice.createBuffer({
                    size: outputBufferSize,
                    usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST
                });
                infoLog?.log(`Pre-allocated output GPU buffer (${outputBufferSize} bytes)`);

                // Create reusable output tensor with pre-allocated GPU buffer
                outputTensor = ort.Tensor.fromGpuBuffer(outputGpuBuffer, {
                    dataType: 'float32',
                    dims: [1, 1, config.inputHeight, config.inputWidth],
                    dispose: () => {
                        // Don't actually dispose the buffer, just mark for potential reuse
                        // The buffer is managed by the segmentation worker
                    }
                });

            } catch (error) {
                errorLog?.log('WebGPU backend failed:', error);
                throw new Error(`WebGPU backend failed: ${error instanceof Error ? error.message : 'Unknown error'}`);
            }

            infoLog?.log('ONNX model loaded successfully');
            infoLog?.log('Model inputs:', session.inputNames);
            infoLog?.log('Model outputs:', session.outputNames);

            // Resolve and cache model config for tensor conversion
            resolvedModelConfig = segmentationConfig.modelConfig ?? getModelConfig(modelUrl);
            infoLog?.log(`Model config: format=${resolvedModelConfig.tensorFormat}, output=${resolvedModelConfig.outputFormat ?? DEFAULT_MODEL_CONFIG.outputFormat}, layout=${resolvedModelConfig.outputLayout ?? DEFAULT_MODEL_CONFIG.outputLayout}`);

            // Initialize frame queue for async processing
            initializeQueue(segmentationConfig);

            processing = true;

        } catch (error) {
            errorLog?.log('Failed to initialize:', error);
            throw error;
        }
    },

    /**
   * Process a single frame with segmentation and background blur
   * Uses frame skipping, motion detection, and queuing optimizations
   */
    // eslint-disable-next-line
    processFrame: async (frame: VideoFrame, _noWait: RpcNoWait): Promise<void> => {
        if (!session || !config || !processing) {
            warnLog?.log('Not initialized or not processing');
            frame.close();
            return; // Return original frame unchanged
        }

        enqueueFrame(frame);
    },

    /**
   * Update segmentation configuration
   */
    // eslint-disable-next-line
    updateConfig: async (newConfig: Partial<SegmentationConfig>): Promise<void> => {
        if (!config) {
            throw new Error('Worker not initialized');
        }

        infoLog?.log('Updating configuration:', newConfig);

        // Update config
        config = { ...config, ...newConfig };

    // Note: For simplicity, we don't recreate the session here
    // In a production implementation, you might want to recreate the session
    // if the backend changes
    },

    /**
   * Get current performance statistics
   */
    // eslint-disable-next-line
    getStats: async (): Promise<SegmentationStats> => {
        return {
            processedFrames,
            averageInferenceTime: processedFrames > 0 ? totalInferenceTime / processedFrames : 0,
            averageBlurTime: processedFrames > 0 ? totalBlurTime / processedFrames : 0,
            averageTotalTime: processedFrames > 0 ? totalProcessingTime / processedFrames : 0,
            droppedFrames,
            backend: config?.backend ?? 'unknown'
        };
    },

    /**
   * Stop processing and clean up resources
   */
    stop: async (): Promise<void> => {
        infoLog?.log('Stopping segmentation worker...');

        processing = false;

        // Await all in-flight async readbacks
        try { await awaitAllPendingReadbacks(); } catch { /* ignore */ }

        // Clear frame queue - close pending frames
        if (frameQueue) {
            while (!frameQueue.isEmpty()) {
                const queuedFrame = frameQueue.shift();
                if (queuedFrame) {
                    queuedFrame.frame.close();
                }
            }
            frameQueue = null;
        }

        // Clean up ONNX session
        if (session) {
            // Note: ONNX Runtime Web doesn't have explicit cleanup methods
            // The session will be garbage collected
            session = null;
        }

        // Clean up GPU buffers
        outputGpuBuffer.destroy();
        smoothedMaskBuffer.destroy();

        // Reset all state
        config = null;
        resolvedModelConfig = null;
        frameCounter = 0;
        hasValidMask = false;
        loggedFormat = false;
        processingFrame = false;
        frameSequence = 0;

        processedFrames = 0;
        totalInferenceTime = 0;
        totalBlurTime = 0;
        totalProcessingTime = 0;
        droppedFrames = 0;

        infoLog?.log('Segmentation worker stopped');
    },

    /**
   * Dispose of the worker resources
   */
    dispose: (): void => {
        infoLog?.log('Disposing segmentation worker...');
    // The RPC system will handle cleanup
    }
};

// Initialize RPC communication
const pipeline = rpcClientServer<SegmentationWorkerCallbacks>(
    'SegmentationWorker',
  self as unknown as Worker,
  serverImpl
);

infoLog?.log('Segmentation worker initialized');
