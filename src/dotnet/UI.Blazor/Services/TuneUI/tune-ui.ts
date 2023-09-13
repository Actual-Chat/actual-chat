import { delayAsync, PromiseSource } from 'promises';
import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { soundPlayer } from './sound-player';
import { Interactive } from 'interactive';

const { logScope, debugLog, warnLog } = Log.get('TuneUI');

// !!! keep in sync with TuneUI.cs
export enum Tune
{
    None = 0,
    CancelReply,
    OpenModal,
    CloseModal,
    SelectNavbarItem,
    ShowInputError,
    BeginRecording,
    EndRecording,
    RemindOfRecording,
    StartRealtimePlayback,
    StartHistoricalPlayback,
    StopHistoricalPlayback,
    StopRealtimePlayback,
    PinUnpinChat,
    SendMessage,
    EditMessage,
    ReplyMessage,
    ChangeAttachments,
    SelectPrimaryLanguage,
    SelectSecondaryLanguage,
    ShowMenu
}

export type TuneName = keyof typeof Tune;

interface TuneInfo { vibration: Array<number>, sound?: string }

export class TuneUI {
    private static whenReady = new PromiseSource<{ [key in TuneName]: TuneInfo }>();

    public static init(tunes: { [key in TuneName]: TuneInfo }){
        this.whenReady.resolve(tunes);
    }

    public static play(tune: Tune, vibrate = true): void {
        void this.playAndWait(tune, vibrate);
    }

    public static async playAndWait(tuneKey: Tune, vibrate = true): Promise<void> {
        const tunes = await this.whenReady;
        const tune = tunes[tuneKey];

        if (!tune)
            throw new Error(`${logScope}.playAndWait: unexpected tune ${tuneKey}.`);

        return Promise.race([
                                vibrate ? this.playVibration(tuneKey, tune) : null,
                                this.playSound(tuneKey, tune)]);
    }

    // Private methods

    private static async playVibration(tuneKey: Tune, tune: TuneInfo): Promise<void> {
        if (!tune.vibration) {
            warnLog?.log(`playVibration: no vibration for tune '${tuneKey}'`);
            return;
        }
        else
            debugLog?.log(`playVibration: '${tuneKey}'`);

        for (let i = 0; i < tune.vibration.length; i++) {
            const durationMs = tune[i];
            if (i % 2 == 0)
                await this.vibrateAndWait(durationMs);
            else
                await delayAsync(durationMs);
        }
    }

    private static async playSound(tuneKey: Tune, tune: TuneInfo): Promise<void> {
        if (!tune.sound) {
            return;
        }
        else
            debugLog?.log(`playSound: '${tuneKey}'`);

        const ext = DeviceInfo.isWebKit ? '.m4a' : '.webm'; // TODO: allow webm for iOS >= 16.5
        const soundUrl = `dist/sounds/${tune.sound}${ext}`;
        await soundPlayer.play(soundUrl);
    }

    private static vibrate(durationMs: number = 20): void {
        const canVibrate = ('vibrate' in navigator);
        if (!canVibrate) {
            debugLog?.log(`vibrate(${durationMs}ms): unsupported by browser`);
            return;
        }

        if (!Interactive.isInteractive) {
            debugLog?.log(`vibrate(${durationMs}ms): suppressed (non-interactive yet)`);
            return;
        }

        if (!window.navigator.vibrate(durationMs)) {
            warnLog?.log(`vibrate(${durationMs}ms): suppressed by browser`);
            return;
        }

        debugLog?.log(`vibrate(${durationMs}ms)`);
    }

    private static vibrateAndWait(durationMs: number): Promise<void> {
        this.vibrate(durationMs);
        return delayAsync(durationMs);
    }
}
