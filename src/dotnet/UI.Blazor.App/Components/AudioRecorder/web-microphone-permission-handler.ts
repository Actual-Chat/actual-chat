import { tryQueryPermissionState } from 'permissions';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';
import { DeviceInfo } from '../../../../nodejs/src/device-info';
import { OpusMediaRecorder } from './opus-media-recorder';
import { Log } from '../../../../nodejs/src/logging';

const { debugLog, warnLog, errorLog } = Log.get('AudioRecorder');
export class WebMicrophonePermissionHandler {

    /** Called from Blazor  */
    public static async checkPermission(): Promise<PermissionState> {
        debugLog?.log(`-> checkPermission()`);
        try {
            const hasMicrophone = await WebMicrophonePermissionHandler.hasMicrophone();
            debugLog?.log(`checkPermission(): hasMicrophone=`, hasMicrophone);

            const state = await tryQueryPermissionState('microphone');
            if (state !== null)
                return state;

            return hasMicrophone
                ? 'prompt'
                : 'denied';
        }
        finally {
            debugLog?.log(`<- checkPermission()`);
        }
    }

    /** Called from Blazor  */
    public static async requestPermission(): Promise<boolean> {
        debugLog?.log(`-> requestPermission()`);
        try {
            const hasMicrophone = await WebMicrophonePermissionHandler.hasMicrophone();
            const hasPermission = await WebMicrophonePermissionHandler.hasPermission();

            if (!hasMicrophone || !hasPermission) {
                // Requests microphone permission
                let stream: MediaStream | null = null;
                try {
                    debugLog?.log(`requestPermission: detecting active tracks to stop`);
                    if (BrowserInfo.hostKind === 'MauiApp' && DeviceInfo.isIos ) {
                        // iOS MAUI keeps microphone acquired, so let's return true there to avoid user complains
                        return true;
                    }
                    else {
                        // Better integration with native mobile audio pipeline - we are resetting to defaults
                        if ('audioSession' in navigator) {
                            // @ts-ignore
                            navigator.audioSession['type'] = 'auto';
                        }
                        stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
                        await OpusMediaRecorder.stopStreamTracks(stream);
                    }
                }
                catch (error) {
                    errorLog?.log(`requestPermission: failed to request microphone permissions`, error);
                    return false;
                }
                finally {
                    await OpusMediaRecorder.stopStreamTracks(stream);
                    stream = null;
                }

                return true;
            }

            return hasMicrophone;
        }
        finally {
            debugLog?.log(`<- requestPermission()`);
        }
    }

    public static async hasMicrophone(): Promise<boolean> {
        const isMaui = BrowserInfo.hostKind == 'MauiApp';
        let hasMicrophone = false;
        if (navigator.mediaDevices?.enumerateDevices) {
            const devices = await navigator.mediaDevices.enumerateDevices();
            const inputDevices = devices.filter(d => d.kind === 'audioinput');
            const inputDevice = inputDevices.pop();
            hasMicrophone = (inputDevice && inputDevice.deviceId !== '') || isMaui;
        }
        return hasMicrophone;
    }

    public static async hasPermission(): Promise<boolean> {
        return (await tryQueryPermissionState('microphone')) === 'granted';
    }
}
