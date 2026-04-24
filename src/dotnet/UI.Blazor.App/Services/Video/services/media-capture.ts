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
            videoConstraints.frameRate = { ideal: options.frameRate, max: options.frameRate };
        }
        if (options.width && options.height) {
            // Camera sensors are physically landscape. Always request in sensor
            // orientation (large × small) regardless of device orientation —
            // portrait output is produced later by rotating via VideoFrame.rotation.
            // Use `ideal` + generous `max`: exact-match fails on devices that only
            // expose binned modes, and the GPU downscaler handles oversampling.
            const large = Math.max(options.width, options.height);
            const small = Math.min(options.width, options.height);
            videoConstraints.width = { ideal: large, max: large * 2 };
            videoConstraints.height = { ideal: small, max: large * 2 };
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
