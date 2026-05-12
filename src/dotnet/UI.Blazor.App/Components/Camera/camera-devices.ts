import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';

const { infoLog, errorLog } = getLogs('CameraDevices');

export interface VideoDevice {
    deviceId: string;
    label: string;
}

export class CameraDevices {
    static async enumerateDevices(includeAll = false): Promise<VideoDevice[]> {
        try {
            const videoInputs = (await navigator.mediaDevices.enumerateDevices())
                .filter(d => d.kind === 'videoinput');
            const selected = DeviceInfo.isMobile && !includeAll
                ? CameraDevices.pickMobileCameras(videoInputs)
                : videoInputs;
            const videoDevices = selected.map(d => ({
                deviceId: d.deviceId,
                label: d.label || `Camera ${d.deviceId.slice(0, 8)}`,
            }));
            infoLog?.log('Enumerated video devices:', videoDevices);
            return videoDevices;
        } catch (error) {
            errorLog?.log('Failed to enumerate video devices:', error);
            return [];
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
