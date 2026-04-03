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
    static async captureCameraStream(options: CameraCaptureOptions = {}): Promise<MediaStreamTrack> {
        const videoConstraints: MediaTrackConstraints = {};
        if (options.deviceId) {
            videoConstraints.deviceId = { exact: options.deviceId };
        }
        if (options.frameRate) {
            videoConstraints.frameRate = { ideal: options.frameRate };
        }
        infoLog?.log('captureCameraStream: constraints:', JSON.stringify(videoConstraints));
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
                    infoLog?.log(`captureCameraStream: camera busy, retrying in ${delayMs}ms (attempt ${attempt + 1}/${maxRetries})`);
                    await new Promise(resolve => setTimeout(resolve, delayMs));
                    continue;
                }
                infoLog?.log('captureCameraStream: failed to capture camera stream. Error:', JSON.stringify(e), (e as OverconstrainedError).constraint);
                throw e;
            }
        }

        const initialSettings = videoTrack.getSettings();
        infoLog?.log(`captureCameraStream: initial ${initialSettings.width}x${initialSettings.height}`);

        // Apply requested resolution via applyConstraints, adapting to stream orientation
        if (options.width && options.height && initialSettings.width && initialSettings.height) {
            const isPortrait = initialSettings.height > initialSettings.width;
            // Requested dimensions assume landscape; swap for portrait streams
            const targetWidth = isPortrait ? options.height : options.width;
            const targetHeight = isPortrait ? options.width : options.height;

            const orientationMatches = isPortrait
                ? initialSettings.height > initialSettings.width
                : initialSettings.width >= initialSettings.height;
            const hasMatchingDimension = initialSettings.width === targetWidth || initialSettings.height === targetHeight;
            if (orientationMatches && hasMatchingDimension) {
                infoLog?.log(`captureCameraStream: close enough at ${initialSettings.width}x${initialSettings.height} (target ${targetWidth}x${targetHeight}), skipping applyConstraints`);
            } else {
                try {
                    infoLog?.log(`captureCameraStream: applying ${targetWidth}x${targetHeight} (portrait=${isPortrait})`);
                    await videoTrack.applyConstraints({
                        width: { ideal: targetWidth },
                        height: { ideal: targetHeight },
                    });
                    const adjusted = videoTrack.getSettings();
                    infoLog?.log(`captureCameraStream: after applyConstraints ${adjusted.width}x${adjusted.height}`);
                } catch (e) {
                    // applyConstraints failed — track stays in its previous state per spec
                    const kept = videoTrack.getSettings();
                    infoLog?.log(`captureCameraStream: applyConstraints failed, keeping ${kept.width}x${kept.height}. Error:`, e);
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
