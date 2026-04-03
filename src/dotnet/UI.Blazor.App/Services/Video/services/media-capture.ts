import { Log } from 'logging';

const { infoLog } = Log.get('VideoRecorder');

export interface CameraCaptureOptions {
    deviceId?: string;
    width?: number;
    height?: number;
    frameRate?: number;
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
        // NOTE(DF): To think how to handle width/height options.
        // Given size typically has album orientation. But on smartphones camera video might be in portrait orientation.
        // if (options.width) {
        //     videoConstraints.width = { ideal: options.width };
        // }
        // if (options.height) {
        //     videoConstraints.height = { ideal: options.height };
        // }
        infoLog?.log('captureCameraStream: constraints:', JSON.stringify(videoConstraints));
        try {
            const stream = await navigator.mediaDevices.getUserMedia({
                video: videoConstraints,
                audio: false,
            });
            const videoTrack = stream.getVideoTracks()[0];
            const settings = videoTrack.getSettings();
            infoLog?.log('captureCameraStream: camera stream settings:', JSON.stringify(settings));
            return videoTrack;
        }
        catch (e) {
            infoLog?.log('captureCameraStream: failed to capture camera stream. Error:', JSON.stringify(e), (e as OverconstrainedError).constraint);
            throw e;
        }
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
