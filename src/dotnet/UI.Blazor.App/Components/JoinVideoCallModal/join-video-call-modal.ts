import { Log } from 'logging';
import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import { getActiveRecorder, type VideoDevice } from '../VideoPanel/video-recorder';
import type { VideoProcessingWorker, VideoProcessingWorkerCallbacks } from '../../Services/Video/workers/video-processing-worker-contract';
import { createAdaptiveSegmentationConfig } from '../../Services/Video/workers/video-processing-worker-contract';
import { detectGPUBackends } from '../../Services/Video/gpu-support';
import { MediaCapture } from '../../Services/Video/services/media-capture';
import { Versioning } from 'versioning';
import { fastRaf } from 'fast-raf';

const { infoLog, errorLog } = Log.get('VideoRecorder');

export class JoinVideoCallModal {
    private blazorRef: DotNet.DotNetObject;
    private readonly container: HTMLElement;
    private readonly videoEl: HTMLVideoElement; // off-DOM, frame source only
    private readonly canvasEl: HTMLCanvasElement; // on-DOM, single display canvas
    private readonly canvasCtx: CanvasRenderingContext2D | null;
    private track: MediaStreamTrack | null = null;
    private selectedDeviceId: string | null = null;
    private isRendering = false;
    private lastTrackStoppedAt = 0;
    private attachedFromRecorder = false;

    // Blur preview state
    private previewWorkerInstance: Worker | null = null;
    private previewWorker: (VideoProcessingWorker & Disposable) | null = null;
    private isBlurActive = false;
    private blurFrameTimer: number | null = null;
    private captureCanvas: HTMLCanvasElement;
    private captureCtx: CanvasRenderingContext2D;
    private firstFrameNotified = false;

    static create(container: HTMLElement, blazorRef: DotNet.DotNetObject): JoinVideoCallModal {
        return new JoinVideoCallModal(container, blazorRef);
    }

    static async enumerateDevices(): Promise<VideoDevice[]> {
        try {
            // Request permission first to get device labels
            const tempStream = await navigator.mediaDevices.getUserMedia({ video: true });
            tempStream.getTracks().forEach(t => t.stop());

            const devices = await navigator.mediaDevices.enumerateDevices();
            const videoDevices = devices
                .filter(d => d.kind === 'videoinput')
                .map(d => ({
                    deviceId: d.deviceId,
                    label: d.label || `Camera ${d.deviceId.slice(0, 8)}`,
                }));
            infoLog?.log('Enumerated video devices:', videoDevices);
            return videoDevices;
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

    public async startPreview(deviceId?: string): Promise<boolean> {
        await this.stopPreview();

        // Browser needs time to release camera hardware after track.stop().
        // Wait if a track was recently stopped (within the last 2 seconds).
        const timeSinceStop = performance.now() - this.lastTrackStoppedAt;
        if (this.lastTrackStoppedAt > 0 && timeSinceStop < 2000) {
            const delay = Math.max(300 - timeSinceStop, 0);
            if (delay > 0)
                await new Promise(resolve => setTimeout(resolve, delay));
        }

        try {
            this.track = await MediaCapture.captureCameraStream({
                deviceId: deviceId,
                maxRetries: 3,
            });

            // Capture the actual device ID the browser chose (important when no
            // explicit device was requested — ensures recording uses the same camera)
            const s = this.track.getSettings();
            infoLog?.log(`startPreview track: deviceId=${s.deviceId}, ${s.width}x${s.height}`);
            if (s.deviceId) this.selectedDeviceId = s.deviceId;

            this.videoEl.srcObject = new MediaStream([this.track]);
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
        this.firstFrameNotified = false;

        if (this.track) {
            this.track.stop();
            this.track = null;
            this.lastTrackStoppedAt = performance.now();
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
        if (this.track) {
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
     * Get the actual device ID of the currently-previewing camera.
     * Returns the device ID resolved by getUserMedia, which may differ
     * from what was originally requested (e.g., when no device was specified).
     */
    public getActualDeviceId(): string | null {
        return this.selectedDeviceId;
    }

    /**
     * Attach to the active recorder's preview stream instead of acquiring a new one.
     * Returns true if successfully attached, false if no active recorder.
     */
    public attachFromRecorder(): boolean {
        const recorder = getActiveRecorder();
        if (!recorder) return false;

        const previewStream = recorder.getPreviewStream();
        if (!previewStream) return false;

        // Clone the track so we can stop it independently
        const originalTrack = previewStream.getVideoTracks()[0];
        this.track = originalTrack.clone();
        this.attachedFromRecorder = true;

        // Pause the recorder's own preview rendering
        recorder.pausePreviewRendering();

        this.videoEl.srcObject = new MediaStream([this.track]);
        void this.videoEl.play();

        // Insert canvas into the video-frame container
        const frame = this.container.querySelector('.video-frame');
        if (frame) {
            frame.querySelector<HTMLElement>('.plug-text')!.style.display = 'none';
            frame.appendChild(this.canvasEl);
        }

        this.startRenderLoop();
        infoLog?.log('Attached to active recorder preview stream');
        return true;
    }

    /**
     * Detach from the recorder's stream without stopping the recorder.
     */
    public detachFromRecorder(): void {
        if (!this.attachedFromRecorder) return;

        // Stop blur preview first
        void this.stopBlurPreview();
        this.stopRenderLoop();

        // Stop only our cloned track
        if (this.track) {
            this.track.stop();
            this.track = null;
        }
        this.videoEl.srcObject = null;

        if (this.canvasEl.parentElement)
            this.canvasEl.parentElement.removeChild(this.canvasEl);

        // Restore placeholder text
        const frame = this.container.querySelector('.video-frame');
        if (frame)
            frame.querySelector<HTMLElement>('.plug-text')!.style.display = '';

        // Resume the recorder's own preview rendering
        const recorder = getActiveRecorder();
        if (recorder) recorder.resumePreviewRendering();

        this.attachedFromRecorder = false;
        infoLog?.log('Detached from recorder preview stream');
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

                // Notify Blazor on first frame
                if (!this.firstFrameNotified) {
                    this.firstFrameNotified = true;
                    void this.blazorRef.invokeMethodAsync('OnFirstFrameRendered');
                }
            }
        }

        fastRaf(this.renderFrame, 'join-video-preview');
    };

    private async startBlurPreview(): Promise<void> {
        if (!this.track || this.isBlurActive) return;

        try {
            infoLog?.log('Starting blur preview...');

            const gpuSupport = await detectGPUBackends();
            const segConfig = createAdaptiveSegmentationConfig(gpuSupport.recommended);

            // Use unified video processing worker in preview-only mode
            const workerPath = Versioning.mapPath('/dist/videoProcessingWorker.js');
            this.previewWorkerInstance = new Worker(workerPath, { type: 'module' });

            this.previewWorker = rpcClientServer<VideoProcessingWorker>(
                'PreviewBlur',
                this.previewWorkerInstance,
                {
                    onPreviewFrame: (frame: VideoFrame) => {
                        if (this.canvasCtx && this.isBlurActive) {
                            if (this.canvasEl.width !== frame.displayWidth ||
                                this.canvasEl.height !== frame.displayHeight) {
                                this.canvasEl.width = frame.displayWidth;
                                this.canvasEl.height = frame.displayHeight;
                            }
                            this.canvasCtx.drawImage(frame as CanvasImageSource, 0, 0);
                        }
                        frame.close();
                        return Promise.resolve();
                    },
                    onSerializedChunk: () => Promise.resolve(),
                    onBackpressure: () => Promise.resolve(),
                    onEncoderFailed: () => Promise.resolve(),
                    onDimensionReconciled: () => Promise.resolve(),
                    onStreamCreated: () => Promise.resolve(),
                } as VideoProcessingWorkerCallbacks
            );

            await this.previewWorker.initialize({
                encoder: { codec: 'av01.0.01M.08', width: 640, height: 360, bitrate: 500_000, framerate: 15, keyframeInterval: 30, latencyMode: 'realtime', hardwareAcceleration: 'prefer-software' },
                segmentation: segConfig,
                streaming: { hubUrl: '', sessionToken: '', chatId: '', serverClockOffsetMs: 0 },
                previewOnly: true,
            }, { type: 'rpc-timeout', timeoutMs: 15000 });

            this.isBlurActive = true;
            this.pumpBlurFrames();

            infoLog?.log('Blur preview started');
        } catch (error) {
            errorLog?.log('Failed to start blur preview:', error);
            await this.stopBlurPreview();
        }
    }

    private pumpBlurFrames(): void {
        if (!this.isBlurActive || !this.track || !this.previewWorker) return;

        if (this.videoEl.videoWidth > 0 && this.videoEl.videoHeight > 0) {
            this.captureCanvas.width = this.videoEl.videoWidth;
            this.captureCanvas.height = this.videoEl.videoHeight;
            this.captureCtx.drawImage(this.videoEl, 0, 0);

            const frame = new VideoFrame(this.captureCanvas, {
                timestamp: performance.now() * 1000
            });

            void this.previewWorker.encodeFrame(frame, rpcNoWait);
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

        if (this.previewWorker) {
            try {
                await this.previewWorker.stop();
                this.previewWorker.dispose();
            } catch {
                // ignore cleanup errors
            }
            this.previewWorker = null;
        }

        if (this.previewWorkerInstance) {
            this.previewWorkerInstance.terminate();
            this.previewWorkerInstance = null;
        }

        infoLog?.log('Blur preview stopped');
    }

    public dispose(): void {
        if (this.attachedFromRecorder) {
            this.detachFromRecorder();
        } else {
            void this.stopBlurPreview();
            void this.stopPreview();
        }
    }
}
