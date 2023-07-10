import { delayAsync } from 'promises';
import { VibrationUI } from '../VibrationUI/vibration-ui';
import { Log } from 'logging';

const { logScope, debugLog, warnLog } = Log.get('TuneUI');

export class TuneUI {
    public static readonly vibrationTunes: { [key: string]: Array<number> } = {
        // General actions
        'cancel': [20],
        'open-modal': [20],
        'close-modal': [20],
        'select-navbar-item': [],
        'input-error': [80],
        // Recording
        'begin-recording': [100,50,50],
        'end-recording': [100],
        // Playback
        'start-realtime-playback': [100],
        'start-historical-playback': [100],
        'stop-historical-playback': [20],
        'stop-realtime-playback': [20],
        // Chat UI
        'pin-unpin-chat': [50],
        // ChatMessageEditor
        'send-message': [50],
        'edit-message': [20],
        'reply-message': [20],
        'change-attachments': [20],
        'select-primary-language': [50, 50, 50],
        'select-secondary-language': [50],
    };

    public static play(tuneName: string): void {
        void this.playAndWait(tuneName);
    }

    public static async playAndWait(tuneName: string): Promise<void> {
        if (this.vibrationTunes[tuneName])
            return this.playVibration(tuneName);

        throw new Error(`${logScope}.playAndWait: not supported yet.`);
    }

    // Private methods

    private static async playVibration(tuneName: string): Promise<void> {
        const tune = this.vibrationTunes[tuneName];
        if (!tune) {
            warnLog?.log(`playVibration: no tune '${tuneName}'`);
            return;
        }
        else
            debugLog?.log(`playVibration: '${tuneName}'`);

        for (let i = 0; i < tune.length; i++) {
            const durationMs = tune[i];
            if (i % 2 == 0)
                await VibrationUI.vibrateAndWait(durationMs);
            else
                await delayAsync(durationMs);
        }
    }
}
