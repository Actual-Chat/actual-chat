import QrScanner from 'qr-scanner';
import { getLogs } from 'logging';
import { CameraDevices, VideoDevice } from '../Camera/camera-devices';
import { MediaCapture } from '../../Services/Video/services/media-capture';

const { debugLog, warnLog } = getLogs('QrScanView');

const ScanIntervalMs = 200;
const CaptureWidth = 1280;
const CaptureHeight = 720;
/**
 * A code lined up with the viewfinder brackets still needs its quiet zone to decode, so the region
 * handed to the decoder is the square grown by this share of its size on every side.
 */
const ScanRegionPadding = 0.15;

export class QrScanView {
    private readonly video: HTMLVideoElement;
    private readonly viewfinder: HTMLElement;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly canvas = document.createElement('canvas');
    // Deprecated is the overload taking a worker path, not the no-arg one this names.
    // eslint-disable-next-line @typescript-eslint/no-deprecated
    private enginePromise: ReturnType<typeof QrScanner.createQrEngine> | null = null;
    private track: MediaStreamTrack | null = null;
    private timer: ReturnType<typeof setInterval> | null = null;
    private lastReported = '';
    private isScanning = false;
    private isDisposed = false;

    static create(
        video: HTMLVideoElement,
        viewfinder: HTMLElement,
        blazorRef: DotNet.DotNetObject,
    ): QrScanView {
        const view = new QrScanView(video, viewfinder, blazorRef);
        void view.start();
        return view;
    }

    constructor(video: HTMLVideoElement, viewfinder: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.video = video;
        this.viewfinder = viewfinder;
        this.blazorRef = blazorRef;
    }

    public dispose(): void {
        if (this.isDisposed)
            return;

        this.isDisposed = true;
        if (this.timer !== null) {
            clearInterval(this.timer);
            this.timer = null;
        }
        this.video.srcObject = null;
        this.track?.stop();
        this.track = null;
        void this.enginePromise?.then(engine => {
            if (engine instanceof Worker)
                engine.terminate();
        });
        this.enginePromise = null;
    }

    // Private methods

    private async start(): Promise<void> {
        try {
            const track = await this.capture();
            if (this.isDisposed) {
                track.stop();
                return;
            }

            this.track = track;
            const settings = track.getSettings();
            debugLog?.log(`start: ${settings.width}x${settings.height}, ${track.label}`);
            this.video.srcObject = new MediaStream([track]);
            // .catch swallows AbortError when srcObject is cleared mid-play (rapid close).
            this.video.play().catch((e: unknown) => debugLog?.log('start: play failed:', e));
            this.enginePromise = QrScanner.createQrEngine();
            this.timer = setInterval(() => void this.scan(), ScanIntervalMs);
        }
        catch (e) {
            warnLog?.log('start: failed to open the camera:', e);
            if (!this.isDisposed)
                void this.blazorRef.invokeMethodAsync('OnCameraFailed');
        }
    }

    private async capture(): Promise<MediaStreamTrack> {
        // The back camera is the one pointed at someone else's screen - but a phone has several, and
        // both the enumeration order and the browser's own pick for the facing can land on the
        // telephoto, a view so tight the code has to be held at arm's length. Android numbers the
        // main wide camera 0, so the lowest-numbered back camera wins; labels without a number (iOS,
        // desktops) leave the choice to the browser.
        const devices = await CameraDevices.enumerateDevices(true);
        const main = QrScanView.pickMainBackCamera(devices);
        return MediaCapture.captureCameraStream({
            deviceId: main?.deviceId,
            facingMode: 'environment',
            width: CaptureWidth,
            height: CaptureHeight,
        });
    }

    private static pickMainBackCamera(devices: VideoDevice[]): VideoDevice | null {
        let main: VideoDevice | null = null;
        let mainNumber = Number.POSITIVE_INFINITY;
        for (const device of devices) {
            if (device.facing !== 'environment')
                continue;

            // "Camera 4" / "Camera2 0" as Android names them; a placeholder label is not a number
            const number = Number(/^camera2? (\d+)$/i.exec(device.label)?.[1] ?? NaN);
            if (Number.isNaN(number) || number >= mainNumber)
                continue;

            main = device;
            mainNumber = number;
        }
        return main;
    }

    // A scan that outruns its interval would queue frames the user has already moved past, so the
    // ticks that arrive while one is decoding are dropped rather than awaited.
    private async scan(): Promise<void> {
        if (this.isScanning || this.video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA)
            return;

        const scanRegion = this.getScanRegion();
        if (scanRegion === null)
            return;

        this.isScanning = true;
        try {
            const result = await QrScanner.scanImage(this.video, {
                scanRegion,
                qrEngine: this.enginePromise,
                canvas: this.canvas,
                returnDetailedScanResult: true,
            });
            // A code sitting in the frame decodes on every tick; the one the user didn't act on
            // stays theirs to ignore rather than being reported five times a second.
            if (this.isDisposed || result.data === this.lastReported)
                return;

            this.lastReported = result.data;
            await this.blazorRef.invokeMethodAsync('OnScanned', result.data);
        }
        catch {
            // scanImage throws QrScanner.NO_QR_CODE_FOUND for every frame without a code, which is
            // most of them - nothing to report and nothing to do.
        }
        finally {
            this.isScanning = false;
        }
    }

    // The decoder sees the viewfinder square only: object-fit: cover crops most of a landscape frame
    // on a portrait pane, so a code just entering at the edge is long since whole in the frame.
    private getScanRegion(): QrScanner.ScanRegion | null {
        const { videoWidth, videoHeight } = this.video;
        const videoRect = this.video.getBoundingClientRect();
        const viewfinderRect = this.viewfinder.getBoundingClientRect();
        if (videoWidth === 0 || videoHeight === 0 || videoRect.width === 0 || videoRect.height === 0)
            return null;

        const scale = Math.max(videoRect.width / videoWidth, videoRect.height / videoHeight);
        const cropX = (videoWidth * scale - videoRect.width) / 2;
        const cropY = (videoHeight * scale - videoRect.height) / 2;
        const padding = viewfinderRect.width * ScanRegionPadding;
        const x = (viewfinderRect.left - videoRect.left - padding + cropX) / scale;
        const y = (viewfinderRect.top - videoRect.top - padding + cropY) / scale;
        const left = Math.max(0, Math.round(x));
        const top = Math.max(0, Math.round(y));
        const right = Math.min(videoWidth, Math.round(x + (viewfinderRect.width + 2 * padding) / scale));
        const bottom = Math.min(videoHeight, Math.round(y + (viewfinderRect.height + 2 * padding) / scale));
        if (right <= left || bottom <= top)
            return null;

        return { x: left, y: top, width: right - left, height: bottom - top };
    }
}
