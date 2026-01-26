/**
 * Video Recorder
 * Manages camera capture and coordinates encoding/decoding through web workers.
 * Implements the encode/decode loop for video processing.
 */

import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import { Log } from 'logging';

import type { EncoderWorker, EncoderWorkerCallbacks } from './workers/encoder-worker-contract';
import type { DecoderWorker, DecoderWorkerCallbacks } from './workers/decoder-worker-contract';
import type { EncodedChunkData } from './webcodecs-encoder';
import {
    type VideoRecorderConfig,
    type VideoRecorderState,
    type VideoRecorderCallbacks,
    type IVideoRecorder,
    DEFAULT_VIDEO_RECORDER_CONFIG,
    createEncoderConfig,
    createDecoderConfig,
} from './video-recorder-contract';
import { detectSupportedCodecs, getDefaultCodec } from './codec-support';
import { Versioning } from 'versioning';

const { debugLog, infoLog, warnLog, errorLog } = Log.get('VideoRecorder');

/**
 * VideoRecorder class
 * Orchestrates camera capture, encoding, decoding, and frame rendering
 */
export class VideoRecorder implements IVideoRecorder {
    private config: VideoRecorderConfig;
    private callbacks: VideoRecorderCallbacks;
    private state: VideoRecorderState;

    // Camera resources
    private mediaStream: MediaStream | null = null;
    private videoTrack: MediaStreamTrack | null = null;
    private trackProcessor: any = null;
    private frameReader: ReadableStreamDefaultReader<VideoFrame> | null = null;

    // Workers
    private encoderWorker: Worker | null = null;
    private decoderWorker: Worker | null = null;
    private encoderClient: (EncoderWorker & Disposable) | null = null;
    private decoderClient: (DecoderWorker & Disposable) | null = null;

    // Processing state
    private isProcessing = false;
    private frameCount = 0;
    private decodedFrameCount = 0;

    constructor(callbacks: VideoRecorderCallbacks) {
        this.callbacks = callbacks;
        this.config = { ...DEFAULT_VIDEO_RECORDER_CONFIG };
        this.state = {
            isRecording: false,
            hasCamera: false,
            encoderReady: false,
            decoderReady: false,
            error: null,
            framesEncoded: 0,
            framesDecoded: 0,
        };
    }

    /**
     * Initialize the recorder with configuration
     */
    async initialize(config?: Partial<VideoRecorderConfig>): Promise<void> {
        try {
            infoLog?.log('Initializing VideoRecorder...');

            // Merge config with defaults
            if (config) {
                this.config = { ...this.config, ...config };
            }

            // Detect best codec if not specified
            if (!config?.codec) {
                infoLog?.log('Detecting supported codecs...');
                const supportedCodecs = await detectSupportedCodecs(this.config.width, this.config.height);
                // TODO(AK): Do not use other codecs except default one
                // this.config.codec = getDefaultCodec(supportedCodecs);
                infoLog?.log(`Selected codec: ${this.config.codec}`);
            }

            // Request camera access
            await this.initializeCamera();

            // Initialize workers
            await this.initializeWorkers();

            this.updateState({ error: null });
            infoLog?.log('VideoRecorder initialized successfully');
        } catch (error) {
            const errorMessage = error instanceof Error ? error.message : String(error);
            errorLog?.log('Failed to initialize VideoRecorder:', error);
            this.updateState({ error: errorMessage });
            this.callbacks.onError(error instanceof Error ? error : new Error(errorMessage));
            throw error;
        }
    }

    /**
     * Initialize camera access
     */
    private async initializeCamera(): Promise<void> {
        infoLog?.log('Requesting camera access...');

        const constraints: MediaStreamConstraints = {
            video: {
                width: { ideal: this.config.width },
                height: { ideal: this.config.height },
                frameRate: { ideal: this.config.frameRate },
                ...(this.config.deviceId && { deviceId: { exact: this.config.deviceId } }),
            },
            audio: false,
        };

        try {
            this.mediaStream = await navigator.mediaDevices.getUserMedia(constraints);
            this.videoTrack = this.mediaStream.getVideoTracks()[0];

            if (!this.videoTrack) {
                throw new Error('No video track available');
            }

            // Get actual track settings
            const settings = this.videoTrack.getSettings();
            infoLog?.log(`Camera initialized: ${settings.width}x${settings.height} @ ${settings.frameRate}fps`);

            // Update config with actual values if different
            if (settings.width && settings.height) {
                this.config.width = settings.width;
                this.config.height = settings.height;
            }
            if (settings.frameRate) {
                this.config.frameRate = settings.frameRate;
            }

            this.updateState({ hasCamera: true });
        } catch (error) {
            errorLog?.log('Failed to access camera:', error);
            this.updateState({ hasCamera: false });
            throw error;
        }
    }

    /**
     * Initialize encoder and decoder workers
     */
    private async initializeWorkers(): Promise<void> {
        infoLog?.log('Initializing workers...');

        // Create encoder worker
        const encoderWorkerPath = Versioning.mapPath('/dist/videoEncoderWorker.js');
        this.encoderWorker = new Worker(encoderWorkerPath, { type: 'module' });

        // Create decoder worker
        const decoderWorkerPath = Versioning.mapPath('/dist/videoDecoderWorker.js');
        this.decoderWorker = new Worker(decoderWorkerPath, { type: 'module' });

        // Set up encoder RPC client with callbacks
        const encoderCallbacks: EncoderWorkerCallbacks = {
            onEncodedChunk: async (chunkData: EncodedChunkData) => {
                this.frameCount++;
                this.updateState({ framesEncoded: this.frameCount });

                // Forward encoded chunk to decoder
                if (this.decoderClient && this.isProcessing) {
                    await this.decoderClient.decodeChunk(chunkData);
                }
            },
        };

        this.encoderClient = rpcClientServer<EncoderWorkerCallbacks>(
            'VideoRecorder.encoder',
            this.encoderWorker,
            encoderCallbacks
        ) as unknown as EncoderWorker & Disposable;

        // Set up decoder RPC client with callbacks
        const decoderCallbacks: DecoderWorkerCallbacks = {
            onDecodedFrame: async (frame: VideoFrame) => {
                this.decodedFrameCount++;
                this.updateState({ framesDecoded: this.decodedFrameCount });

                // Send frame to UI for rendering
                try {
                    this.callbacks.onFrame(frame);
                } finally {
                    frame.close();
                }
            },
        };

        this.decoderClient = rpcClientServer<DecoderWorkerCallbacks>(
            'VideoRecorder.decoder',
            this.decoderWorker,
            decoderCallbacks
        ) as unknown as DecoderWorker & Disposable;

        // Initialize encoder
        const encoderConfig = createEncoderConfig(this.config);
        infoLog?.log('Initializing encoder with config:', encoderConfig);
        await this.encoderClient.initialize(encoderConfig);
        this.updateState({ encoderReady: true });

        // Initialize decoder
        const decoderConfig = createDecoderConfig(this.config);
        infoLog?.log('Initializing decoder with config:', decoderConfig);
        await this.decoderClient.initialize(decoderConfig);
        this.updateState({ decoderReady: true });

        infoLog?.log('Workers initialized successfully');
    }

    /**
     * Start recording and processing video frames
     */
    async start(): Promise<void> {
        if (this.state.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        if (!this.videoTrack) {
            throw new Error('Camera not initialized');
        }

        if (!this.encoderClient || !this.decoderClient) {
            throw new Error('Workers not initialized');
        }

        infoLog?.log('Starting video recording...');

        this.isProcessing = true;
        this.frameCount = 0;
        this.decodedFrameCount = 0;
        this.updateState({
            isRecording: true,
            framesEncoded: 0,
            framesDecoded: 0,
        });

        // Create MediaStreamTrackProcessor for efficient frame extraction
        // @ts-expect-error - MediaStreamTrackProcessor may not be in TypeScript types yet
        this.trackProcessor = new MediaStreamTrackProcessor({ track: this.videoTrack });
        this.frameReader = this.trackProcessor.readable.getReader();

        // Start frame processing loop
        this.processFrames();

        infoLog?.log('Video recording started');
    }

    /**
     * Process frames from camera in a loop
     */
    private async processFrames(): Promise<void> {
        if (!this.frameReader || !this.encoderClient) {
            return;
        }

        try {
            while (this.isProcessing) {
                const { value: frame, done } = await this.frameReader.read();

                if (done) {
                    infoLog?.log('Frame reader completed');
                    break;
                }

                if (!frame) {
                    continue;
                }

                if (!this.isProcessing) {
                    frame.close();
                    break;
                }

                try {
                    // Send frame to encoder worker (frame is transferred, not copied)
                    await this.encoderClient.encodeFrame(frame);
                } catch (error) {
                    warnLog?.log('Error encoding frame:', error);
                    frame.close();
                }
            }
        } catch (error) {
            if (this.isProcessing) {
                errorLog?.log('Error in frame processing loop:', error);
                this.callbacks.onError(error instanceof Error ? error : new Error(String(error)));
            }
        }
    }

    /**
     * Stop recording
     */
    async stop(): Promise<void> {
        if (!this.state.isRecording) {
            return;
        }

        infoLog?.log('Stopping video recording...');

        this.isProcessing = false;

        // Cancel frame reader
        if (this.frameReader) {
            try {
                await this.frameReader.cancel();
            } catch (error) {
                warnLog?.log('Error canceling frame reader:', error);
            }
            this.frameReader = null;
        }

        // Close track processor
        this.trackProcessor = null;

        // Flush encoder and decoder
        if (this.encoderClient) {
            try {
                await this.encoderClient.flush();
            } catch (error) {
                warnLog?.log('Error flushing encoder:', error);
            }
        }

        if (this.decoderClient) {
            try {
                await this.decoderClient.flush();
            } catch (error) {
                warnLog?.log('Error flushing decoder:', error);
            }
        }

        this.updateState({ isRecording: false });
        infoLog?.log('Video recording stopped');
    }

    /**
     * Get current state
     */
    getState(): VideoRecorderState {
        return { ...this.state };
    }

    /**
     * Dispose of all resources
     */
    dispose(): void {
        infoLog?.log('Disposing VideoRecorder...');

        // Stop recording if active
        if (this.state.isRecording) {
            this.isProcessing = false;
        }

        // Cancel frame reader
        if (this.frameReader) {
            this.frameReader.cancel().catch(() => {});
            this.frameReader = null;
        }

        // Stop encoder worker
        if (this.encoderClient) {
            this.encoderClient.stop().catch(() => {});
            this.encoderClient.dispose();
            this.encoderClient = null;
        }

        // Stop decoder worker
        if (this.decoderClient) {
            this.decoderClient.stop().catch(() => {});
            this.decoderClient.dispose();
            this.decoderClient = null;
        }

        // Terminate workers
        if (this.encoderWorker) {
            this.encoderWorker.terminate();
            this.encoderWorker = null;
        }

        if (this.decoderWorker) {
            this.decoderWorker.terminate();
            this.decoderWorker = null;
        }

        // Stop camera
        if (this.videoTrack) {
            this.videoTrack.stop();
            this.videoTrack = null;
        }

        if (this.mediaStream) {
            this.mediaStream.getTracks().forEach(track => track.stop());
            this.mediaStream = null;
        }

        this.updateState({
            isRecording: false,
            hasCamera: false,
            encoderReady: false,
            decoderReady: false,
        });

        infoLog?.log('VideoRecorder disposed');
    }

    /**
     * Update state and notify callbacks
     */
    private updateState(updates: Partial<VideoRecorderState>): void {
        this.state = { ...this.state, ...updates };
        this.callbacks.onStateChange(this.state);
    }
}

/**
 * Factory function to create a VideoRecorder instance
 */
export function createVideoRecorder(callbacks: VideoRecorderCallbacks): IVideoRecorder {
    return new VideoRecorder(callbacks);
}
