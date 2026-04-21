import { getLogs } from 'logging';
import { rpcClientServer, rpcNoWait } from 'rpc';
import type { Disposable } from 'disposable';
import { getActiveRecorder } from '../VideoPanel/video-recorder';
import type { VideoProcessingWorker, VideoProcessingWorkerCallbacks } from '../../Services/Video/workers/video-processing-worker-contract';
import { createAdaptiveSegmentationConfig } from '../../Services/Video/workers/video-processing-worker-contract';
import { detectGPUBackends } from '../../Services/Video/gpu-support';
import { MediaCapture } from '../../Services/Video/services/media-capture';
import { Versioning } from 'versioning';
import { fastRaf } from 'fast-raf';

const { infoLog, warnLog, errorLog } = getLogs('JoinVideoCallModal');

export class JoinVideoCallModal {
    private blazorRef: DotNet.DotNetObject;
    private readonly container: HTMLElement;
    private readonly videoEl: HTMLVideoElement; // off-DOM, frame source only
    private readonly canvasEl: HTMLCanvasElement; // on-DOM, single display canvas
    private readonly canvasCtx: CanvasRenderingContext2D | null;
    private readonly captureCanvas: HTMLCanvasElement;
    private track: MediaStreamTrack | null = null;
    private selectedDeviceId: string | null = null;
    private currentFacingMode: string | null = null;
    private isRendering = false;
    private lastTrackStoppedAt = 0;
    private attachedFromRecorder = false;

    // Blur preview state
    private previewWorkerInstance: Worker | null = null;
    private previewWorker: (VideoProcessingWorker & Disposable) | null = null;
    private isBlurActive = false;
    private blurFrameTimer: number | null = null;
    private captureCtx: CanvasRenderingContext2D;
    private firstFrameNotified = false;
    private disposed = false;

    static create(container: HTMLElement, blazorRef: DotNet.DotNetObject): JoinVideoCallModal {
        return new JoinVideoCallModal(container, blazorRef);
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

    public startPreview(deviceId?: string): Promise<boolean> {
        return this.startPreviewInternal({ deviceId });
    }

    private async startPreviewInternal(
        options: { deviceId?: string; facingMode?: 'user' | 'environment' },
    ): Promise<boolean> {
        infoLog?.log(`startPreview: deviceId=${options.deviceId ?? '(default)'}, facingMode=${options.facingMode ?? '(none)'}`);
        await this.stopPreview();

        // Browser needs time to release camera hardware after track.stop().
        // Wait if a track was recently stopped (within the last 2 seconds).
        const timeSinceStop = performance.now() - this.lastTrackStoppedAt;
        if (this.lastTrackStoppedAt > 0 && timeSinceStop < 2000) {
            const delay = Math.max(300 - timeSinceStop, 0);
            if (delay > 0)
                await new Promise(resolve => setTimeout(resolve, delay));
        }

        if (this.disposed)
            return false;

        try {
            const track = await MediaCapture.captureCameraStream({
                deviceId: options.deviceId,
                facingMode: options.facingMode,
                // When picking by facingMode, bias toward the highest-resolution
                // camera for that facing (main lens vs ultrawide/tele).
                preferHighRes: options.facingMode != null && options.deviceId == null,
                maxRetries: 3,
            });
            // If the modal was closed while getUserMedia was in flight, stop the
            // freshly-acquired track immediately — otherwise it holds the camera
            // hardware and the next getUserMedia fails with NotReadableError.
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            if (this.disposed) {
                warnLog?.log('startPreview: disposed during getUserMedia — stopping track');
                track.stop();
                this.lastTrackStoppedAt = performance.now();
                return false;
            }
            this.track = track;

            // Capture the actual device ID the browser chose (important when no
            // explicit device was requested — ensures recording uses the same camera)
            const s = this.track.getSettings();
            infoLog?.log(`startPreview track: deviceId=${s.deviceId}, ${s.width}x${s.height}, facingMode=${s.facingMode ?? '(none)'}`);
            if (s.deviceId) this.selectedDeviceId = s.deviceId;
            this.currentFacingMode = s.facingMode ?? null;

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
        infoLog?.log(`switchCamera: deviceId=${deviceId}`);
        this.selectedDeviceId = deviceId;
        const wasBlurActive = this.isBlurActive;
        // Restart preview with new device (stopPreview cleans up blur)
        const success = await this.startPreview(deviceId);
        // Restart blur if it was active
        if (success && wasBlurActive) {
            await this.startBlurPreview();
        }
        return success;
    }

    public async switchFacing(): Promise<boolean> {
        // Toggle between front ('user') and back ('environment') on mobile.
        // Picks by facingMode rather than deviceId so the browser selects the
        // primary lens for each facing (avoids cycling main/ultrawide/tele).
        // Returns false if the opposite facing isn't available on this device.
        const target: 'user' | 'environment' =
            this.currentFacingMode === 'environment' ? 'user' : 'environment';
        infoLog?.log(`switchFacing: ${this.currentFacingMode ?? '(unknown)'} -> ${target}`);
        const wasBlurActive = this.isBlurActive;
        const success = await this.startPreviewInternal({ facingMode: target });
        if (success) {
            this.selectedDeviceId = this.track?.getSettings().deviceId ?? null;
            if (wasBlurActive)
                await this.startBlurPreview();
        }
        return success;
    }

    /**
     * Get the actual device ID of the currently-previewing camera.
     * Returns the device ID resolved by getUserMedia, which may differ
     * from what was originally requested (e.g., when no device was specified).
     */
    public getActualDeviceId(): string | null {
        return this.selectedDeviceId;
    }

    public getCurrentCameraInfo(): { deviceId: string | null; facingMode: string | null } {
        // Consumed by Blazor to resolve per-camera display preferences (mirror etc.).
        return { deviceId: this.selectedDeviceId, facingMode: this.currentFacingMode };
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

    public setMirrored(mirrored: boolean): void {
        // Toggles a CSS class on .video-frame that overrides scaleX(-1).
        const frame = this.container.querySelector<HTMLElement>('.video-frame');
        if (frame)
            frame.classList.toggle('no-mirror', !mirrored);
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
                if (!this.firstFrameNotified && !this.disposed) {
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
                streaming: { apiUrl: '', sessionToken: '', chatId: '', serverClockOffsetMs: 0 },
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
        infoLog?.log(`dispose: trackState=${this.track?.readyState ?? '(null)'}`);
        this.disposed = true;
        if (this.attachedFromRecorder) {
            this.detachFromRecorder();
        } else {
            void this.stopBlurPreview();
            void this.stopPreview();
        }
    }
}
