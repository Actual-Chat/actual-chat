import { tryQueryPermissionState } from 'permissions';
import { getLogs } from 'logging';

const { debugLog, errorLog } = getLogs('CameraPermission');

export class WebCameraPermissionHandler {

    /** Called from Blazor */
    public static async checkPermission(): Promise<PermissionState> {
        debugLog?.log(`-> checkPermission()`);
        try {
            const state = await tryQueryPermissionState('camera');
            if (state !== null)
                return state;

            // Permissions API absent (Firefox, some embedded WebViews) — fall back
            // to enumerateDevices: labels are filled in only after permission was
            // ever granted, so a non-empty label means we have permission.
            const hasCameraWithLabel = await WebCameraPermissionHandler.hasCameraWithLabel();
            return hasCameraWithLabel ? 'granted' : 'prompt';
        }
        finally {
            debugLog?.log(`<- checkPermission()`);
        }
    }

    /** Called from Blazor */
    public static async requestPermission(): Promise<boolean> {
        debugLog?.log(`-> requestPermission()`);
        let stream: MediaStream | null = null;
        try {
            stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
            return true;
        }
        catch (error) {
            errorLog?.log(`requestPermission: failed to request camera permission`, error);
            return false;
        }
        finally {
            stream?.getTracks().forEach(t => t.stop());
            debugLog?.log(`<- requestPermission()`);
        }
    }

    private static async hasCameraWithLabel(): Promise<boolean> {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!navigator.mediaDevices?.enumerateDevices)
            return false;
        const devices = await navigator.mediaDevices.enumerateDevices();
        return devices.some(d => d.kind === 'videoinput' && !!d.label);
    }
}
