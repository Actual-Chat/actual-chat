import { tryQueryPermissionState } from 'permissions';
import { getLogs } from 'logging';

const { errorLog } = getLogs('LocationTracker');

export class WebLocationPermissionHandler {

    /** Called from Blazor */
    public static async checkPermission(): Promise<PermissionState> {
        const state = await tryQueryPermissionState('geolocation');
        return state ?? 'prompt';
    }

    /** Called from Blazor */
    public static requestPermission(): Promise<boolean> {
        return new Promise<boolean>(resolve => {
            navigator.geolocation.getCurrentPosition(
                () => resolve(true),
                error => {
                    errorLog?.log('requestPermission: geolocation error', error);
                    resolve(false);
                },
                { enableHighAccuracy: true, timeout: 10000 });
        });
    }
}
