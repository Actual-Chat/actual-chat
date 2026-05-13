import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';

const { warnLog, errorLog } = getLogs('CameraDevices');

export interface VideoDevice {
    deviceId: string;
    label: string;
}

export class CameraDevices {
    static async enumerateDevices(includeAll = false): Promise<VideoDevice[]> {
        try {
            let videoInputs = await CameraDevices.enumerateVideoInputs();
            // Before camera permission is exercised in this browsing session
            // (notably iOS Safari and some Android browsers), enumerateDevices
            // returns placeholder entries with empty deviceId/label. Probe with
            // a brief getUserMedia to make the browser populate real device
            // info, then re-enumerate.
            const hasPlaceholders = videoInputs.some(d => !d.deviceId);
            if (hasPlaceholders) {
                await CameraDevices.probeCameraPermission();
                videoInputs = await CameraDevices.enumerateVideoInputs();
            }
            const selected = DeviceInfo.isMobile && !includeAll
                ? CameraDevices.pickMobileCameras(videoInputs)
                : videoInputs;
            return selected.map(d => ({
                deviceId: d.deviceId,
                label: d.label || `Camera ${d.deviceId.slice(0, 8)}`,
            }));
        } catch (error) {
            errorLog?.log('Failed to enumerate video devices:', error);
            return [];
        }
    }

    private static async enumerateVideoInputs(): Promise<MediaDeviceInfo[]> {
        return (await navigator.mediaDevices.enumerateDevices())
            .filter(d => d.kind === 'videoinput');
    }

    // Briefly opens a camera stream to make the browser populate deviceId /
    // label fields on subsequent enumerateDevices calls. The track is stopped
    // immediately so it doesn't hold the camera hardware against an imminent
    // startPreview getUserMedia.
    private static async probeCameraPermission(): Promise<void> {
        let stream: MediaStream | null = null;
        try {
            stream = await navigator.mediaDevices.getUserMedia({ video: true });
        } catch (e) {
            warnLog?.log('probeCameraPermission: getUserMedia failed:', e);
        } finally {
            stream?.getTracks().forEach(t => t.stop());
        }
    }

    // On mobile, picks one front + one back camera (preferring `facingMode` from
    // getCapabilities, falling back to label heuristics). Avoids surfacing
    // duplicate "wide" / "telephoto" entries that confuse end users.
    private static pickMobileCameras(devices: MediaDeviceInfo[]): MediaDeviceInfo[] {
        const facingOf = (d: MediaDeviceInfo): 'user' | 'environment' | null => {
            const input = d as InputDeviceInfo;
            const facing: string[] | undefined = typeof input.getCapabilities === 'function'
                ? input.getCapabilities().facingMode
                : undefined;
            if (facing && facing.length > 0) {
                if (facing.includes('user')) return 'user';
                if (facing.includes('environment')) return 'environment';
            }
            const label = d.label.toLowerCase();
            if (/facing front|\bfront\b|\buser\b|self/.test(label)) return 'user';
            if (/facing back|\bback\b|\brear\b|environment/.test(label)) return 'environment';
            return null;
        };

        const front = devices.find(d => facingOf(d) === 'user');
        const back = devices.find(d => facingOf(d) === 'environment');
        if (front && back)
            return [front, back];
        if (front || back)
            return [front ?? back!, ...devices.filter(d => d !== (front ?? back) && facingOf(d) === null).slice(0, 1)];
        return devices.slice(0, 2);
    }
}
