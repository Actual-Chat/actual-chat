import { VIDEO } from 'app-constants';
import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { DeviceOrientation } from 'orientation';

const { infoLog } = getLogs('VideoRecorder');

export interface CameraCaptureOptions {
    deviceId?: string;
    width?: number;
    height?: number;
    frameRate?: number;
    maxRetries?: number;
}

const PreacquireConsumeTimeoutMs = 120_000;

export class MediaCapture {
    private static nextCaptureId = 0;
    private static pendingScreenCast: Promise<MediaStreamTrack> | null = null;

    static async captureCameraStream(options: CameraCaptureOptions = {}): Promise<MediaStreamTrack> {
        const captureId = ++MediaCapture.nextCaptureId;
        const tag = `captureCameraStream#${captureId}`;
        const maxRetries = options.maxRetries ?? 0;

        let lastError: unknown;
        for (const candidate of MediaCapture.buildCameraConstraintCandidates(options)) {
            infoLog?.log(`${tag}: ${candidate.name} constraints:`, JSON.stringify(candidate.constraints));
            try {
                const videoTrack = await MediaCapture.captureVideoTrack(candidate.constraints, maxRetries, tag);
                const initialSettings = videoTrack.getSettings();
                infoLog?.log(`${tag}: ${candidate.name} initial ${initialSettings.width}x${initialSettings.height}`);

                if (candidate.minimumSize && !MediaCapture.meetsMinimumSize(initialSettings, candidate.minimumSize)) {
                    infoLog?.log(`${tag}: ${candidate.name} returned below requested minimum — retrying with next candidate`);
                    videoTrack.stop();
                    continue;
                }

                return videoTrack;
            }
            catch (e) {
                lastError = e;
                if (candidate.fallbackOnFailure) {
                    infoLog?.log(`${tag}: ${candidate.name} failed, trying next candidate. Error:`, JSON.stringify(e, ['name', 'message', 'constraint']));
                    continue;
                }
                infoLog?.log(`${tag}: failed to capture camera stream. Error:`, JSON.stringify(e, ['name', 'message', 'constraint']));
                throw e;
            }
        }

        infoLog?.log(`${tag}: failed to capture camera stream. Error:`, JSON.stringify(lastError, ['name', 'message', 'constraint']));
        throw lastError;
    }

    // Renegotiate a live track's frame rate in place (no getUserMedia — a
    // second acquire wedges Android/iOS Safari). applyConstraints REPLACES
    // the whole constraint set, so the base is the track's ACQUISITION
    // constraints (getConstraints), not dims rebuilt from getSettings():
    // re-deriving dims re-runs mode selection without the original
    // portrait/landscape candidate shape, and Android then satisfies a
    // portrait request by crop-and-scaling a landscape mode — flipped and
    // zoomed. `ideal` rate lets drivers with discrete modes (Android:
    // 15/30) snap to the nearest one instead of failing.
    static async applyFrameRate(track: MediaStreamTrack, frameRate: number): Promise<boolean> {
        const base = track.getConstraints();
        try {
            await track.applyConstraints({ ...base, frameRate: { ideal: frameRate } });
            return true;
        }
        catch (e) {
            infoLog?.log(
                `applyFrameRate: ${frameRate}fps rejected. Error:`,
                JSON.stringify(e, ['name', 'message', 'constraint']));
            return false;
        }
    }

    private static async captureVideoTrack(
        videoConstraints: MediaTrackConstraints,
        maxRetries: number,
        tag: string,
    ): Promise<MediaStreamTrack> {
        for (let attempt = 0; ; attempt++) {
            try {
                const stream = await navigator.mediaDevices.getUserMedia({
                    video: videoConstraints,
                    audio: false,
                });
                return stream.getVideoTracks()[0];
            }
            catch (e) {
                const isDeviceBusy = e instanceof DOMException
                    && (e.name === 'NotReadableError' || e.name === 'AbortError');
                if (isDeviceBusy && attempt < maxRetries) {
                    const delayMs = 300 * (attempt + 1);
                    infoLog?.log(`${tag}: camera busy, retrying in ${delayMs}ms (attempt ${attempt + 1}/${maxRetries})`);
                    await new Promise(resolve => setTimeout(resolve, delayMs));
                    continue;
                }
                throw e;
            }
        }
    }

    private static buildCameraConstraintCandidates(options: CameraCaptureOptions): CameraConstraintCandidate[] {
        const requestedLarge = options.width && options.height
            ? Math.max(options.width, options.height)
            : 0;
        const requestedSmall = options.width && options.height
            ? Math.min(options.width, options.height)
            : 0;
        const hasExplicitCap = requestedLarge > 0 && requestedSmall > 0;
        const targetLarge = hasExplicitCap ? requestedLarge : 1280;
        const targetSmall = hasExplicitCap ? requestedSmall : 720;
        // Allow the browser headroom above `ideal`. A tight `max = targetLarge`
        // forces an exact match — many phone cameras can't deliver arbitrary
        // small dims (e.g. 360×640 portrait), causing the portrait candidate
        // to fail and the next (landscape) candidate to center-crop a
        // 16:9 band out of the portrait sensor. `normalizeFrame` cover-crops
        // any oversize result to the top-layer dims.
        const max = targetLarge * 2;
        // Keep min generous so virtual / cheap cameras with limited
        // resolution sets can still negotiate down.
        const minLarge = Math.min(targetLarge, 1280);
        const minSmall = Math.min(targetSmall, 720);
        const landscape: CameraConstraintCandidate = {
            name: `native ${targetLarge}x${targetSmall} landscape`,
            constraints: MediaCapture.buildCameraConstraints(options, {
                width: { min: minLarge, ideal: targetLarge, max },
                height: { min: minSmall, ideal: targetSmall, max },
            }),
            minimumSize: { large: minLarge, small: minSmall },
            fallbackOnFailure: true,
        };
        const portrait: CameraConstraintCandidate = {
            name: `native ${targetSmall}x${targetLarge} portrait`,
            constraints: MediaCapture.buildCameraConstraints(options, {
                width: { min: minSmall, ideal: targetSmall, max },
                height: { min: minLarge, ideal: targetLarge, max },
            }),
            minimumSize: { large: minLarge, small: minSmall },
            fallbackOnFailure: true,
        };
        const strictCandidates = MediaCapture.preferPortraitConstraint()
            ? [portrait, landscape]
            : [landscape, portrait];
        return [
            ...strictCandidates,
            {
                name: `permissive ${targetLarge}x${targetSmall}`,
                constraints: MediaCapture.buildCameraConstraints(options, {
                    width: { ideal: targetLarge, max },
                    height: { ideal: targetSmall, max },
                }),
                fallbackOnFailure: false,
            },
        ];
    }

    private static buildCameraConstraints(
        options: CameraCaptureOptions,
        size?: { width: ConstrainULongRange; height: ConstrainULongRange },
    ): MediaTrackConstraints {
        const videoConstraints: MediaTrackConstraints = {};
        if (options.deviceId)
            videoConstraints.deviceId = { exact: options.deviceId };
        // ideal only, NO max: a hard max below a supported camera mode (Android
        // exposes discrete 15/30 fps ranges) forces the 15-fps mode; the worker's
        // temporalPace enforces the actual target fps downstream instead.
        if (options.frameRate)
            videoConstraints.frameRate = { ideal: options.frameRate };
        // Capture and the IPC and compositing it drives are ~0.33 of the 0.41 cores a video
        // call adds over an audio one, so the sensor/ISP format sits upstream of all of them
        // and is worth asking about.
        //
        // Speculative, and known NOT to work on an iPhone 13 Pro / iOS 26.5: there the track
        // reports `powerEfficient: false` with capabilities `[false, true]`, but the property
        // is read-only telemetry - `{ exact: true }` is rejected with `TypeError: A required
        // constraint`, and `{ ideal: true }` leaves the setting at `false`. Kept because an
        // ideal costs nothing where it is ignored and other devices may honour it. Do not
        // treat this as a landed optimisation; re-check `getSettings().powerEfficient`
        // before claiming it does anything.
        if (DeviceInfo.isMobile) {
            (videoConstraints as MediaTrackConstraints & { powerEfficient?: ConstrainBoolean })
                .powerEfficient = { ideal: true };
        }
        if (size) {
            videoConstraints.width = size.width;
            videoConstraints.height = size.height;
            // Mobile: keep `resizeMode: 'none'`. The ladder is built
            // landscape-first; portrait-vs-landscape detection happens AFTER
            // capture (VideoRecorder flips the ladder when cameraHeight >
            // cameraWidth). If we let the browser `crop-and-scale` to satisfy
            // the landscape constraint on a portrait-held phone, it
            // center-crops a 16:9 band out of the portrait sensor — tightly
            // cropped to the speaker's face. `resizeMode: 'none'` makes the
            // browser deliver native sensor frames so orientation detection
            // can run on the real aspect ratio.
            //
            // Desktop / OBS: drop the override so the browser honors `max=`
            // and downscales to top-layer dims at source. This lets
            // `normalizeFrame` short-circuit (no per-frame canvas draw /
            // VideoFrame allocation) — a load-bearing optimisation, since
            // per-frame top-dim VideoFrame creation builds GPU texture
            // pressure that strangles the NVENC HW encoder over time.
            if (DeviceInfo.isMobile) {
                (videoConstraints as MediaTrackConstraints & { resizeMode?: ConstrainDOMString })
                    .resizeMode = { ideal: 'none' };
            }
        }
        return videoConstraints;
    }

    private static meetsMinimumSize(settings: MediaTrackSettings, minimumSize: CameraMinimumSize): boolean {
        if (!settings.width || !settings.height)
            return true;
        return Math.max(settings.width, settings.height) >= minimumSize.large
            && Math.min(settings.width, settings.height) >= minimumSize.small;
    }

    // Drives constraint orientation by *device pose* (not screen orientation),
    // so the camera gets to deliver its best view for how the phone is held —
    // independent of OS rotation lock and independent of front/rear. iOS
    // stays landscape: MSTP doesn't auto-rotate there, and requesting portrait
    // mid-startup flips the encoder and produces mis-oriented output.
    private static preferPortraitConstraint(): boolean {
        if (DeviceInfo.isIos) return false;
        if (!DeviceInfo.isMobile) return false;
        const q = DeviceOrientation.quarter;
        return q === 0 || q === 2;
    }

    // Acquires the screen track within the current transient user activation and
    // caches it for the next captureScreenCast(). MUST run synchronously from a DOM
    // gesture handler: Safari rejects getDisplayMedia ("must be called from a user
    // gesture handler") once an async hop — here the Blazor server round-trip that
    // reaches startScreenCast — has consumed the activation. See ScreenShareGesture.
    static preacquireScreenCast(): void {
        MediaCapture.discardPendingScreenCast();
        const pending = MediaCapture.requestDisplayMedia();
        MediaCapture.pendingScreenCast = pending;
        // Backstop: stop the track if the consumer never picks it up (e.g. the
        // Blazor flow aborted before startScreenCast, leaving it sharing silently).
        self.setTimeout(() => {
            if (MediaCapture.pendingScreenCast === pending)
                MediaCapture.discardPendingScreenCast();
        }, PreacquireConsumeTimeoutMs);
    }

    static discardPendingScreenCast(): void {
        const pending = MediaCapture.pendingScreenCast;
        if (!pending)
            return;

        MediaCapture.pendingScreenCast = null;
        pending.then(track => track.stop()).catch(() => undefined);
    }

    static async captureScreenCast(): Promise<MediaStreamTrack> {
        const pending = MediaCapture.pendingScreenCast;
        if (pending) {
            MediaCapture.pendingScreenCast = null;
            infoLog?.log('captureScreenCast: using pre-acquired (in-gesture) screen track');
            return pending;
        }
        return MediaCapture.requestDisplayMedia();
    }

    private static async requestDisplayMedia(): Promise<MediaStreamTrack> {
        infoLog?.log(`requestDisplayMedia: requesting display media @ ${VIDEO.frameRate}fps`);
        const stream = await navigator.mediaDevices.getDisplayMedia({
            video: {
                displaySurface: 'monitor',
                frameRate: { ideal: VIDEO.frameRate, max: VIDEO.frameRate },
            },
            audio: false,
        });
        const track = stream.getVideoTracks()[0];
        // 'text' biases the encoder toward sharpness over motion smoothness.
        track.contentHint = 'text';
        const settings = track.getSettings();
        infoLog?.log(`requestDisplayMedia: ${settings.width}x${settings.height} @ ${settings.frameRate}fps, surface=${settings.displaySurface}`);
        return track;
    }
}

interface CameraConstraintCandidate {
    name: string;
    constraints: MediaTrackConstraints;
    minimumSize?: CameraMinimumSize;
    fallbackOnFailure: boolean;
}

interface CameraMinimumSize {
    large: number;
    small: number;
}
