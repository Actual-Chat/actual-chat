import { delayAsync, PromiseSource } from 'promises';
import { Log } from 'logging';
import { DeviceInfo } from 'device-info';
import { SoundPlayer } from './sound-player';
import { Interactive } from 'interactive';

const { logScope, debugLog, warnLog, errorLog } = Log.get('TuneUI');

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
    NotifyOnNewMessageInApp,
    NotifyOnNewAudioMessageAfterDelay,
    SendMessage,
    EditMessage,
    ReplyMessage,
    ChangeAttachments,
    ChangeLanguage,
    ShowMenu,
    React,
    DragStart,
}

export type TuneName = keyof typeof Tune;

interface TuneInfo { vibration: Array<number>, sound?: string }

const cooldownMap = new Map<Tune, number>([
        [Tune.CancelReply, 1],
        [Tune.OpenModal, 1],
        [Tune.CloseModal, 1],
        [Tune.SelectNavbarItem, 1],
        [Tune.ShowInputError, 1],
        [Tune.BeginRecording, 1],
        [Tune.EndRecording, 1],
        [Tune.RemindOfRecording, 1],
        [Tune.StartRealtimePlayback, 1],
        [Tune.StartHistoricalPlayback, 1],
        [Tune.StopHistoricalPlayback, 1],
        [Tune.StopRealtimePlayback, 1],
        [Tune.NotifyOnNewMessageInApp, 5],
        [Tune.NotifyOnNewAudioMessageAfterDelay, 5],
        [Tune.SendMessage, 1],
        [Tune.EditMessage, 1],
        [Tune.ReplyMessage, 1],
        [Tune.ChangeAttachments, 1],
        [Tune.ChangeLanguage, 1],
        [Tune.ShowMenu, 1],
        [Tune.React, 1],
        [Tune.DragStart, 1],
    ]);

export class TuneUI {
    private static whenReady = new PromiseSource();
    private static useJsVibration: boolean;
    private static blazorRef: DotNet.DotNetObject;
    private static tunes: { [key in Tune]: TuneInfo };
    private static readonly soundPlayer = new SoundPlayer();


    /** Called by blazor */
    public static async init(blazorRef: DotNet.DotNetObject, tunes: { [key in Tune]: TuneInfo }, useJsVibration: boolean): Promise<void>{
        this.blazorRef = blazorRef;
        this.tunes = tunes;
        this.useJsVibration = useJsVibration;
        this.whenReady.resolve(null);
    }

    /** Called by blazor */
    public static play(tune: Tune): void {
        void this.playAndWait(tune);
    }

    /** Called by blazor */
    public static async playAndWait(tune: Tune): Promise<void> {
        try {
            await this.whenReady;
            const tuneInfo = this.tunes[tune] ?? this.tunes[Tune[tune]];

            if (!tuneInfo)
            {
                errorLog?.log(`${logScope}.playAndWait: unexpected tune ${tune}.`)
                return;
            }

            await Promise.all([this.playVibration(tune, tuneInfo), this.playSound(tune, tuneInfo)]);
        } catch (e) {
            warnLog?.log('Failed yo play tune', tune, e);
        }
    }

    // Private methods

    private static async playVibration(tune: Tune, tuneInfo: TuneInfo): Promise<void> {
        if (!tuneInfo.vibration) {
            warnLog?.log(`playVibration: no vibration for tune '${tune}'`);
            return;
        }

        debugLog?.log(`playVibration: '${tune}'`);
        if (!this.useJsVibration) {
            await this.blazorRef.invokeMethodAsync('OnVibrate', tune);
            return;
        }

        for (let i = 0; i < tuneInfo.vibration.length; i++) {
            const durationMs = tuneInfo.vibration[i];
            if (i % 2 == 0)
                await this.vibrateAndWait(durationMs);
            else
                await delayAsync(durationMs);
        }
    }

    private static async playSound(tune: Tune, tuneInfo: TuneInfo): Promise<void> {
        if (!tuneInfo.sound) {
            return;
        }
        else
            debugLog?.log(`playSound: '${tune}'`);

        const ext = DeviceInfo.isWebKit ? '.m4a' : '.webm'; // TODO: allow webm for iOS >= 16.5
        const soundUrl = `dist/sounds/${tuneInfo.sound}${ext}`;
        const cooldown = cooldownMap.get(tune);
        await TuneUI.soundPlayer.play(soundUrl, cooldown);
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
