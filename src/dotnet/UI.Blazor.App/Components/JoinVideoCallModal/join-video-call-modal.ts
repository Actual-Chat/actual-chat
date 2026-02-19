import { Log } from 'logging';
import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import type { VideoDevice } from '../VideoPanel/video-recorder';
import type { SegmentationWorker, SegmentationWorkerCallbacks } from '../../Services/Video/workers/segmentation-worker-contract';
import { createAdaptiveSegmentationConfig } from '../../Services/Video/workers/segmentation-worker-contract';
import { detectGPUBackends } from '../../Services/Video/gpu-support';
import { Versioning } from 'versioning';
import { fastRaf } from 'fast-raf';

const { infoLog, errorLog } = Log.get('VideoRecorder');

export class JoinVideoCallModal {
    private blazorRef: DotNet.DotNetObject;
    private readonly container: HTMLElement;
    private readonly videoEl: HTMLVideoElement; // off-DOM, frame source only
    private readonly canvasEl: HTMLCanvasElement; // on-DOM, single display canvas
    private readonly canvasCtx: CanvasRenderingContext2D | null;
    private stream: MediaStream | null = null;
    private selectedDeviceId: string | null = null;
    private isRendering = false;

    // Blur preview state
    private segmentationWorkerInstance: Worker | null = null;
    private segmentationWorker: (SegmentationWorker & Disposable) | null = null;
    private isBlurActive = false;
    private blurFrameTimer: number | null = null;
    private captureCanvas: HTMLCanvasElement;
    private captureCtx: CanvasRenderingContext2D;

    static create(container: HTMLElement, blazorRef: DotNet.DotNetObject): JoinVideoCallModal {
        return new JoinVideoCallModal(container, blazorRef);
    }

    static async enumerateDevices(): Promise<VideoDevice[]> {
        try {
            const devices = await navigator.mediaDevices.enumerateDevices();
            return devices
                .filter(d => d.kind === 'videoinput')
                .map(d => ({
                    deviceId: d.deviceId,
                    label: d.label || `Camera ${d.deviceId.slice(0, 8)}`,
                }));
        } catch (error) {
            errorLog?.log('Failed to enumerate video devices:', error);
            return [];
        }
    }

    constructor(container: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.blazorRef = blazorRef;
        this.container = container;

        // Off-DOM video element — used only as a frame source for canvas drawing
        this.videoEl = document.createElement('video');
        this.videoEl.muted = true;
        this.videoEl.playsInline = true;
        this.videoEl.autoplay = true;

        // Display canvas — inserted into DOM to show camera preview
        this.canvasEl = document.createElement('canvas');
        this.canvasEl.className = 'camera-preview';
        this.canvasCtx = this.canvasEl.getContext('2d');

        // Off-DOM canvas for capturing frames to feed the blur worker
        this.captureCanvas = document.createElement('canvas');
        this.captureCtx = this.captureCanvas.getContext('2d')!;
    }

    public async enumerateVideoDevices(): Promise<VideoDevice[]> {
        try {
            // Request permission first to get device labels
            const tempStream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
            tempStream.getTracks().forEach(t => t.stop());

            const devices = await navigator.mediaDevices.enumerateDevices();
            return devices
                .filter(d => d.kind === 'videoinput')
                .map(d => ({
                    deviceId: d.deviceId,
                    label: d.label || `Camera ${d.deviceId.slice(0, 8)}`,
                }));
        } catch (error) {
            errorLog?.log('Failed to enumerate video devices:', error);
            return [];
        }
    }

    public async startPreview(deviceId?: string): Promise<boolean> {
        await this.stopPreview();

        try {
            const constraints: MediaStreamConstraints = {
                video: deviceId
                    ? { deviceId: { exact: deviceId } }
                    : true,
                audio: false,
            };

            this.stream = await navigator.mediaDevices.getUserMedia(constraints);
            this.videoEl.srcObject = this.stream;
            // Off-DOM video elements don't honor autoplay — must call play() explicitly
            void this.videoEl.play();

            // Insert canvas into the video-frame container
            const frame = this.container.querySelector('.video-frame');
            if (frame) {
                // Hide placeholder text
                frame.querySelector<HTMLElement>('.plug-text')!.style.display = 'none';
                frame.appendChild(this.canvasEl);
            }

            // Start RAF render loop to draw raw camera frames to canvas
            this.startRenderLoop();

            infoLog?.log('Camera preview started');
            return true;
        } catch (error) {
            errorLog?.log('Failed to start camera preview:', error);
            return false;
        }
    }

    public async stopPreview(): Promise<void> {
        // Stop blur preview first
        await this.stopBlurPreview();

        this.stopRenderLoop();

        if (this.stream) {
            this.stream.getTracks().forEach(t => t.stop());
            this.stream = null;
        }

        this.videoEl.srcObject = null;

        if (this.canvasEl.parentElement) {
            this.canvasEl.parentElement.removeChild(this.canvasEl);
        }

        // Restore placeholder text
        const frame = this.container.querySelector('.video-frame');
        if (frame) {
            frame.querySelector<HTMLElement>('.plug-text')!.style.display = '';
        }

        infoLog?.log('Camera preview stopped');
    }

    public async switchCamera(deviceId: string): Promise<boolean> {
        this.selectedDeviceId = deviceId;
        if (this.stream) {
            const wasBlurActive = this.isBlurActive;
            // Restart preview with new device (stopPreview cleans up blur)
            const success = await this.startPreview(deviceId);
            // Restart blur if it was active
            if (success && wasBlurActive) {
                await this.startBlurPreview();
            }
            return success;
        }
        return true;
    }

    /**
     * Toggle blur preview on/off.
     * When enabled, starts a segmentation worker to process camera frames
     * and renders the blurred output to the same canvas.
     */
    public async toggleBlur(enabled: boolean): Promise<void> {
        if (enabled && !this.isBlurActive) {
            await this.startBlurPreview();
        } else if (!enabled && this.isBlurActive) {
            await this.stopBlurPreview();
        }
    }

    private startRenderLoop(): void {
        this.stopRenderLoop();
        this.isRendering = true;
        fastRaf(this.renderFrame, 'join-video-preview');
    }

    private stopRenderLoop(): void {
        this.isRendering = false;
    }

    private renderFrame = (): void => {
        if (!this.isRendering || !this.canvasCtx) return;

        // When blur is active, the blur callback renders to canvas instead
        if (!this.isBlurActive) {
            if (this.videoEl.videoWidth > 0 && this.videoEl.videoHeight > 0) {
                if (this.canvasEl.width !== this.videoEl.videoWidth ||
                    this.canvasEl.height !== this.videoEl.videoHeight) {
                    this.canvasEl.width = this.videoEl.videoWidth;
                    this.canvasEl.height = this.videoEl.videoHeight;
                }
                this.canvasCtx.drawImage(this.videoEl, 0, 0);
            }
        }

        fastRaf(this.renderFrame, 'join-video-preview');
    };

    private async startBlurPreview(): Promise<void> {
        if (!this.stream || this.isBlurActive) return;

        try {
            infoLog?.log('Starting blur preview...');

            // Detect GPU backend
            const gpuSupport = await detectGPUBackends();
            const segConfig = createAdaptiveSegmentationConfig(gpuSupport.recommended);

            // Create segmentation worker
            const workerPath = Versioning.mapPath('/dist/videoSegmentationWorker.js');
            this.segmentationWorkerInstance = new Worker(workerPath, { type: 'module' });

            this.segmentationWorker = rpcClientServer<SegmentationWorker>(
                'PreviewBlur',
                this.segmentationWorkerInstance,
                {
                    onFrameProcessed: (frame: VideoFrame) => {
                        // Draw blurred frame to the same canvas
                        if (this.canvasCtx && this.isBlurActive) {
                            if (this.canvasEl.width !== frame.displayWidth ||
                                this.canvasEl.height !== frame.displayHeight) {
                                this.canvasEl.width = frame.displayWidth;
                                this.canvasEl.height = frame.displayHeight;
                            }
                            this.canvasCtx.drawImage(frame as CanvasImageSource, 0, 0);
                        }
                        // Close frame since no encoder takes ownership
                        frame.close();
                    },
                    onError: (error: Error) => {
                        errorLog?.log('Blur preview error:', error);
                    }
                } as SegmentationWorkerCallbacks
            );

            // Initialize worker (loads ONNX model + WebGPU)
            await this.segmentationWorker.initialize(segConfig, { timeoutMs: 15000 });

            this.isBlurActive = true;

            // Start frame pump
            this.pumpBlurFrames();

            infoLog?.log('Blur preview started');
        } catch (error) {
            errorLog?.log('Failed to start blur preview:', error);
            await this.stopBlurPreview();
        }
    }

    private pumpBlurFrames(): void {
        if (!this.isBlurActive || !this.stream || !this.segmentationWorker) return;

        if (this.videoEl.videoWidth > 0 && this.videoEl.videoHeight > 0) {
            this.captureCanvas.width = this.videoEl.videoWidth;
            this.captureCanvas.height = this.videoEl.videoHeight;
            this.captureCtx.drawImage(this.videoEl, 0, 0);

            const frame = new VideoFrame(this.captureCanvas, {
                timestamp: performance.now() * 1000
            });

            void this.segmentationWorker.processFrame(frame, rpcNoWait);
        }

        // ~15fps for preview (sufficient for blur effect)
        this.blurFrameTimer = window.setTimeout(() => this.pumpBlurFrames(), 66);
    }

    private async stopBlurPreview(): Promise<void> {
        this.isBlurActive = false;

        if (this.blurFrameTimer !== null) {
            clearTimeout(this.blurFrameTimer);
            this.blurFrameTimer = null;
        }

        if (this.segmentationWorker) {
            try {
                await this.segmentationWorker.stop();
                this.segmentationWorker.dispose();
            } catch {
                // ignore cleanup errors
            }
            this.segmentationWorker = null;
        }

        if (this.segmentationWorkerInstance) {
            this.segmentationWorkerInstance.terminate();
            this.segmentationWorkerInstance = null;
        }

        infoLog?.log('Blur preview stopped');
    }

    public dispose(): void {
        void this.stopBlurPreview();
        void this.stopPreview();
    }
}
