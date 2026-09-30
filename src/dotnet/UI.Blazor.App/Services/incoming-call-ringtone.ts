import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { Disposable } from 'disposable';
import { SoundPlayer } from '../../UI.Blazor/Services/TuneUI/sound-player';

const { logScope, debugLog } = getLogs('IncomingCallRingtone');

// The looping web ringtone for incoming calls, driven by CallScreensUI on every platform
// except Android (there the native AndroidIncomingCallsBridge owns the ring).
export class IncomingCallRingtone {
    private static ring: Disposable | null = null;

    /** Called by blazor */
    public static start(): void {
        if (this.ring)
            return;

        const ext = DeviceInfo.isWebKit ? '.m4a' : '.webm';
        debugLog?.log(`${logScope}.start`);
        this.ring = SoundPlayer.instance.loop(`dist/sounds/attention_ringtone${ext}`);
    }

    /** Called by blazor */
    public static stop(): void {
        const ring = this.ring;
        if (!ring)
            return;

        this.ring = null;
        debugLog?.log(`${logScope}.stop`);
        ring.dispose();
    }
}
