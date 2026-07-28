import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';

const { logScope, debugLog, warnLog } = getLogs('IncomingCallRingtone');

// The looping web ringtone for incoming calls, driven by IncomingCallUI on every platform
// except Android (there the native AndroidIncomingCallsBridge owns the ring). A single
// looping HTMLAudioElement is enough: it keeps playing until stop() and needs no AudioContext.
export class IncomingCallRingtone {
    private static audio: HTMLAudioElement | null = null;

    /** Called by blazor */
    public static start(): void {
        if (this.audio)
            return;

        const ext = DeviceInfo.isWebKit ? '.m4a' : '.webm';
        const audio = new Audio(`dist/sounds/attention_ringtone${ext}`);
        audio.loop = true;
        this.audio = audio;
        debugLog?.log(`${logScope}.start`);
        audio.play().catch((e: unknown) => warnLog?.log(`${logScope}.start: play failed`, e));
    }

    /** Called by blazor */
    public static stop(): void {
        const audio = this.audio;
        if (!audio)
            return;

        this.audio = null;
        debugLog?.log(`${logScope}.stop`);
        try {
            audio.pause();
            audio.currentTime = 0;
        } catch (e) {
            warnLog?.log(`${logScope}.stop: failed`, e);
        }
    }
}
