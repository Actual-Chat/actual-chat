import { Log } from 'logging';

const { infoLog } = Log.get('VideoRecorder');

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
                infoLog?.log(`${tag}: failed to capture camera stream. Error:`, JSON.stringify(e), (e as OverconstrainedError).constraint);
                throw e;
            }
        }

        const initialSettings = videoTrack.getSettings();
        infoLog?.log(`${tag}: initial ${initialSettings.width}x${initialSettings.height}`);

        // Apply requested resolution via applyConstraints, adapting to stream orientation
        if (options.width && options.height && initialSettings.width && initialSettings.height) {
            const isPortrait = initialSettings.height > initialSettings.width;
            // Requested dimensions assume landscape; swap for portrait streams
            const targetWidth = isPortrait ? options.height : options.width;
            const targetHeight = isPortrait ? options.width : options.height;
            if (targetWidth === initialSettings.width && targetHeight === initialSettings.height) {
                infoLog?.log(`${tag}: ${initialSettings.width}x${initialSettings.height} already matches ${targetWidth}x${targetHeight}, skipping applyConstraints`);
            } else {
                // Fit into target frame preserving original aspect ratio
                const scale = Math.min(
                    targetWidth / initialSettings.width,
                    targetHeight / initialSettings.height,
                );
                const fitWidth = Math.round(initialSettings.width * scale);
                const fitHeight = Math.round(initialSettings.height * scale);
                try {
                    infoLog?.log(`${tag}: fitting ${initialSettings.width}x${initialSettings.height} into ${targetWidth}x${targetHeight} -> ${fitWidth}x${fitHeight}`);
                    await videoTrack.applyConstraints({
                        width: { ideal: fitWidth },
                        height: { ideal: fitHeight },
                    });
                    const adjusted = videoTrack.getSettings();
                    infoLog?.log(`${tag}: after applyConstraints ${adjusted.width}x${adjusted.height}`);
                } catch (e) {
                    // applyConstraints failed — track stays in its previous state per spec
                    const kept = videoTrack.getSettings();
                    infoLog?.log(`${tag}: applyConstraints failed, keeping ${kept.width}x${kept.height}. Error:`, e);
                }
            }
        }

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
