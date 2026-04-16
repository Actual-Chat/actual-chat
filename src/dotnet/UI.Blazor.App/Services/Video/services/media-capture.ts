import { getLogs } from 'logging';

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
        const videoConstraints: MediaTrackConstraints = {};
        if (options.deviceId) {
            videoConstraints.deviceId = { exact: options.deviceId };
        }
        if (options.frameRate) {
            videoConstraints.frameRate = { ideal: options.frameRate };
        }
        if (options.width && options.height) {
            const min = Math.min(options.width, options.height);
            const max = Math.max(options.width, options.height);
            videoConstraints.width = { min: min, max: max };
            videoConstraints.height = { min: min, max: max };
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
