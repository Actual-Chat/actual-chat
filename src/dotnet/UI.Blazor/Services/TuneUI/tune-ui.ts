import { delayAsync } from 'promises';
import { VibrationUI } from '../VibrationUI/vibration-ui';
import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { soundPlayer } from './sound-player';

const { logScope, debugLog, warnLog } = Log.get('TuneUI');

interface Tune { vibration: Array<number>, sound?: string }

export class TuneUI {
    public static readonly tunes: { [key in string]: Tune } = {
        // General actions
        'cancel-reply': { vibration: [20] },
        'open-modal': { vibration: [20] },
        'close-modal': { vibration: [20] },
        'select-navbar-item': { vibration: [] },
        'show-input-error': { vibration: [80] },
        // Recording
        'begin-recording': { vibration: [100,50,50], sound: 'begin-recording' },
        'end-recording': { vibration: [100], sound: 'end-recording' },
        'remind-of-recording': { vibration: [20], sound: 'remind-of-recording' },
        // Playback
        'start-realtime-playback': { vibration: [100] },
        'start-historical-playback': { vibration: [100] },
        'stop-historical-playback': { vibration: [20] },
        'stop-realtime-playback': { vibration: [20] },
        // Chat UI
        'pin-unpin-chat': { vibration: [50] },
        // ChatMessageEditor
        'send-message': { vibration: [50] },
        'edit-message': { vibration: [20] },
        'reply-message': { vibration: [20] },
        'change-attachments': { vibration: [20] },
        'select-primary-language': { vibration: [50, 50, 50] },
        'select-secondary-language': { vibration: [50] },
    };

    public static play(tuneName: string): void {
        void this.playAndWait(tuneName);
    }

    public static async playAndWait(tuneName: string): Promise<void> {
        const tune = this.tunes[tuneName];

        if (!tune)
            throw new Error(`${logScope}.playAndWait: unexpected tune ${tuneName}.`);

        return Promise.race([this.playVibration(tuneName, tune), this.playSound(tuneName, tune)]);
    }

    // Private methods

    private static async playVibration(tuneName: string, tune: Tune): Promise<void> {
        if (!tune.vibration) {
            warnLog?.log(`playVibration: no vibration for tune '${tuneName}'`);
            return;
        }
        else
            debugLog?.log(`playVibration: '${tuneName}'`);

        for (let i = 0; i < tune.vibration.length; i++) {
            const durationMs = tune[i];
            if (i % 2 == 0)
                await VibrationUI.vibrateAndWait(durationMs);
            else
                await delayAsync(durationMs);
        }
    }

    private static async playSound(tuneName: string, tune: Tune): Promise<void> {
        if (!tune.sound) {
            return;
        }
        else
            debugLog?.log(`playSound: '${tuneName}'`);

        const ext = DeviceInfo.isWebKit ? '.m4a' : '.webm'; // TODO: allow webm for iOS >= 16.5
        const soundUrl = `dist/sounds/${tune.sound}${ext}`;
        await soundPlayer.play(soundUrl);
    }
}
