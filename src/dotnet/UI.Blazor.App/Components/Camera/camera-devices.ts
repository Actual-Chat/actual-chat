import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';

const { warnLog, errorLog } = getLogs('CameraDevices');

export type CameraFacing = 'user' | 'environment';

export interface VideoDevice {
    deviceId: string;
    label: string;
    facing: CameraFacing | null;
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
            return selected.map(d => CameraDevices.toVideoDevice(d));
        } catch (error) {
            errorLog?.log('Failed to enumerate video devices:', error);
            return [];
        }
    }

    // Not via enumerateDevices: its getUserMedia probe would raise a permission prompt.
    static async hasCamera(): Promise<boolean> {
        try {
            return (await CameraDevices.enumerateVideoInputs()).length > 0;
        } catch (error) {
            warnLog?.log('hasCamera: enumerateDevices failed:', error);
            return false;
        }
    }

    // Private methods

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

    private static toVideoDevice(d: MediaDeviceInfo): VideoDevice {
        const facing = CameraDevices.facingOf(d);
        const fallback = `Camera ${d.deviceId.slice(0, 8)}`;
        const label = CameraDevices.normalizeLabel(d.label) || fallback;
        return { deviceId: d.deviceId, label, facing };
    }

    private static facingOf(d: MediaDeviceInfo): CameraFacing | null {
        const input = d as InputDeviceInfo;
        const facing: string[] | undefined = typeof input.getCapabilities === 'function'
            ? input.getCapabilities().facingMode
            : undefined;
        if (facing && facing.length > 0) {
            if (facing.includes('user'))
                return 'user';
            if (facing.includes('environment'))
                return 'environment';
        }

        const label = d.label.toLowerCase();
        if (/facing front|\bfront\b|\buser\b|self/.test(label))
            return 'user';
        if (/facing back|\bback\b|\brear\b|environment/.test(label))
            return 'environment';

        return null;
    }

    // Cleans up redundant facing markers (already shown as a badge in the UI)
    // and capitalizes the first letter when the name isn't already (preserves
    // names like "iPhone Camera" where the second char is already uppercase).
    private static normalizeLabel(raw: string): string {
        let s = raw.trim();
        if (!s)
            return '';

        s = s.replace(/,\s*facing\s+(front|back|user|environment)\s*$/i, '');
        s = s.replace(/\bfacing\s+(front|back|user|environment)\b/gi, '');
        s = s.replace(/^(front|back|rear)\s+camera\b/i, 'Camera');
        s = s.replace(/\s{2,}/g, ' ').trim();
        if (!s)
            return '';

        const c0 = s.charAt(0);
        const c1 = s.charAt(1);
        const isUpper = (c: string) => c !== '' && c === c.toUpperCase() && c !== c.toLowerCase();
        if (isUpper(c0) || isUpper(c1))
            return s;

        return c0.toUpperCase() + s.slice(1);
    }

    // On mobile, picks one front + one back camera (preferring `facingMode` from
    // getCapabilities, falling back to label heuristics). Avoids surfacing
    // duplicate "wide" / "telephoto" entries that confuse end users.
    private static pickMobileCameras(devices: MediaDeviceInfo[]): MediaDeviceInfo[] {
        const front = devices.find(d => CameraDevices.facingOf(d) === 'user');
        const back = devices.find(d => CameraDevices.facingOf(d) === 'environment');
        if (front && back)
            return [front, back];
        if (front || back) {
            const known = front ?? back!;
            const unknown = devices.filter(d => d !== known && CameraDevices.facingOf(d) === null);
            return [known, ...unknown.slice(0, 1)];
        }

        return devices.slice(0, 2);
    }
}
