import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';

const { infoLog } = getLogs('VideoRecorder');

export interface CameraCaptureOptions {
    deviceId?: string;
    width?: number;
    height?: number;
    frameRate?: number;
    maxRetries?: number;
}

export class MediaCapture {
    private static nextCaptureId = 0;

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
        const targetLarge = Math.max(requestedLarge, 1280);
        const targetSmall = Math.max(requestedSmall, 720);
        const minLarge = 1280;
        const minSmall = 720;
        const max = targetLarge * 2;
        const landscape: CameraConstraintCandidate = {
            name: 'native 720p landscape',
            constraints: MediaCapture.buildCameraConstraints(options, {
                width: { min: minLarge, ideal: targetLarge, max },
                height: { min: minSmall, ideal: targetSmall, max },
            }),
            minimumSize: { large: minLarge, small: minSmall },
            fallbackOnFailure: true,
        };
        const portrait: CameraConstraintCandidate = {
            name: 'native 720p portrait',
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
                name: 'permissive native 720p',
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
        if (options.frameRate)
            videoConstraints.frameRate = { ideal: options.frameRate, max: options.frameRate };
        if (size) {
            videoConstraints.width = size.width;
            videoConstraints.height = size.height;
            // Force native capture — no browser-side crop-and-scale. Chrome's
            // `resizeMode: 'crop-and-scale'` returns MSTP VideoFrames with a
            // codedW/H / plane0 mismatch that blows up WebGPU
            // importExternalTexture ("cropSize exceeds texture size"). We do
            // all scaling ourselves in the GPU downscaler; if the camera can't
            // match the requested dims exactly, we'd rather get a larger
            // native frame and downscale than have the browser scale it with
            // the broken metadata.
            (videoConstraints as MediaTrackConstraints & { resizeMode?: ConstrainDOMString })
                .resizeMode = { ideal: 'none' };
        }
        return videoConstraints;
    }

    private static meetsMinimumSize(settings: MediaTrackSettings, minimumSize: CameraMinimumSize): boolean {
        if (!settings.width || !settings.height)
            return true;
        return Math.max(settings.width, settings.height) >= minimumSize.large
            && Math.min(settings.width, settings.height) >= minimumSize.small;
    }

    // iOS Safari MSTP: never request portrait. Sensor is physically landscape and
    // VideoFrame.rotation is left null — the rotation reconcile in
    // workers/video-processing.ts streamReadLoop (isRotated / isRotatedByCoded)
    // and webgpu-downscaler.ts fallbackRotationDeg path only work with
    // sensor-landscape frames. Asking iOS for portrait causes the encoder to
    // flip to landscape mid-startup, producing mis-oriented encoded output.
    // Android Chrome populates VideoFrame.rotation correctly, so portrait
    // capture in portrait orientation is safe there.
    private static preferPortraitConstraint(): boolean {
        if (DeviceInfo.isIos) return false;
        if (!DeviceInfo.isMobile) return false;
        return screen.orientation.type.startsWith('portrait');
    }

    static async captureScreencast(): Promise<MediaStreamTrack> {
        infoLog?.log('captureScreencast: requesting display media');
        // 15 fps ideal (30 max) biases the encoder toward spatial fidelity for
        // IDE/code review content. displaySurface='monitor' is a hint only —
        // browsers may still let the user pick a window/tab.
        const stream = await navigator.mediaDevices.getDisplayMedia({
            video: {
                displaySurface: 'monitor',
                frameRate: { ideal: 15, max: 30 },
            },
            audio: false,
        });
        const track = stream.getVideoTracks()[0];
        // contentHint='text' tells the encoder to prioritize sharpness over
        // motion smoothness — critical for readable text on low-bitrate links.
        track.contentHint = 'text';
        const settings = track.getSettings();
        infoLog?.log(`captureScreencast: ${settings.width}x${settings.height} @ ${settings.frameRate}fps, surface=${settings.displaySurface}`);
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
