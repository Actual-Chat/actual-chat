import { Log } from 'logging';
import { type VideoDevice } from '../VideoPanel/video-panel';

const { infoLog, errorLog } = Log.get('VideoRecorder');

export class JoinVideoCallModal {
    private blazorRef: DotNet.DotNetObject;
    private readonly container: HTMLElement;
    private readonly videoEl: HTMLVideoElement;
    private stream: MediaStream | null = null;
    private selectedDeviceId: string | null = null;

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

        // Create a hidden video element for rendering the camera preview
        this.videoEl = document.createElement('video');
        this.videoEl.className = 'camera-preview';
        this.videoEl.muted = true;
        this.videoEl.playsInline = true;
        this.videoEl.autoplay = true;
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

            // Insert video element into the video-frame container
            const frame = this.container.querySelector('.video-frame');
            if (frame) {
                // Hide placeholder text
                const plug = frame.querySelector('.plug-text') as HTMLElement | null;
                if (plug) plug.style.display = 'none';

                frame.appendChild(this.videoEl);
            }

            infoLog?.log('Camera preview started');
            return true;
        } catch (error) {
            errorLog?.log('Failed to start camera preview:', error);
            return false;
        }
    }

    public async stopPreview(): Promise<void> {
        if (this.stream) {
            this.stream.getTracks().forEach(t => t.stop());
            this.stream = null;
        }

        if (this.videoEl.parentElement) {
            this.videoEl.parentElement.removeChild(this.videoEl);
        }
        this.videoEl.srcObject = null;

        // Restore placeholder text
        const frame = this.container.querySelector('.video-frame');
        if (frame) {
            const plug = frame.querySelector('.plug-text') as HTMLElement | null;
            if (plug) plug.style.display = '';
        }

        infoLog?.log('Camera preview stopped');
    }

    public async switchCamera(deviceId: string): Promise<boolean> {
        this.selectedDeviceId = deviceId;
        if (this.stream) {
            // Restart preview with new device
            return await this.startPreview(deviceId);
        }
        return true;
    }

    public dispose(): void {
        void this.stopPreview();
    }
}
