import { getLogs } from 'logging';

const { infoLog } = getLogs('VideoRecorder');

export interface CameraCaptureOptions {
    deviceId?: string;
    // When set (and deviceId is not), constrains the browser to pick a camera
    // matching this facingMode ('user' = front, 'environment' = back).
    facingMode?: 'user' | 'environment';
    width?: number;
    height?: number;
    frameRate?: number;
    maxRetries?: number;
    // Bias the browser toward the highest-resolution camera matching other
    // constraints. Useful on phones with multiple rear lenses (main / ultrawide /
    // tele) — the browser typically picks the main lens for 4K requests.
    preferHighRes?: boolean;
}

export class MediaCapture {
    private static nextCaptureId = 0;

    static async captureCameraStream(options: CameraCaptureOptions = {}): Promise<MediaStreamTrack> {
        const captureId = ++MediaCapture.nextCaptureId;
        const tag = `captureCameraStream#${captureId}`;
        const videoConstraints: MediaTrackConstraints = {};
        if (options.deviceId) {
            videoConstraints.deviceId = { exact: options.deviceId };
        } else if (options.facingMode) {
            videoConstraints.facingMode = { exact: options.facingMode };
        }
        if (options.frameRate) {
            videoConstraints.frameRate = { ideal: options.frameRate };
        }
        if (options.width && options.height) {
            const min = Math.min(options.width, options.height);
            const max = Math.max(options.width, options.height);
            videoConstraints.width = { min: min, max: max };
            videoConstraints.height = { min: min, max: max };
        } else if (options.preferHighRes) {
            // Bias toward the highest-resolution camera without pinning exact
            // dimensions, so the browser can still honor facingMode / deviceId.
            // Using the same "long side" ideal for both width and height is
            // orientation-agnostic: Android often reports portrait dimensions
            // (width < height) while iOS and desktop report landscape. Fitness
            // distance minimizes |actual - ideal| / ideal per axis, and a large
            // ideal on both axes pulls the browser toward the sensor's maximum
            // resolution regardless of which axis it calls "width".
            videoConstraints.width = { ideal: 3840 };
            videoConstraints.height = { ideal: 3840 };
        }
        infoLog?.log(`${tag}: constraints:`, JSON.stringify(videoConstraints));
        const maxRetries = options.maxRetries ?? 0;
        let videoTrack: MediaStreamTrack;
        for (let attempt = 0; ; attempt++) {
            try {
                const stream = await navigator.mediaDevices.getUserMedia({
                    video: videoConstraints,
                    audio: false,
                });
                videoTrack = stream.getVideoTracks()[0];
                break;
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
                infoLog?.log(`${tag}: failed to capture camera stream. Error:`, JSON.stringify(e, ['name', 'message', 'constraint']));
                throw e;
            }
        }

        const initialSettings = videoTrack.getSettings();
        infoLog?.log(`${tag}: initial ${initialSettings.width}x${initialSettings.height}`);

        return videoTrack;
    }

    static async captureScreencast(): Promise<MediaStreamTrack> {
        infoLog?.log('captureScreencast: requesting display media');
        const stream = await navigator.mediaDevices.getDisplayMedia({
            video: true,
            audio: false,
        });
        return stream.getVideoTracks()[0];
    }
}
