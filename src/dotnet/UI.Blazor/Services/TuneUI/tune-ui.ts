import { delayAsync, PromiseSource } from 'actuallab-core';
import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { SoundPlayer } from './sound-player';
import { Interactive } from 'interactive';

const { logScope, debugLog, warnLog, errorLog } = getLogs('TuneUI');

// !!! keep in sync with TuneUI.cs
export enum Tune {
    None = 0,
    CancelReply,
    OpenModal,
    CloseModal,
    SelectNavbarItem,
    ShowInputError,
    BeginRecording,
    ConfirmRecording,
    EndRecording,
    RemindOfRecording,
    StartListening,
    StartReplay,
    StopReplay,
    StopListening,
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
    ChangeToggle,
    ClickButton,
    PttReplyEnded,
    PttReplyNothingHeard,
    PttGestureDetected,
    PttReplyFailed,
    RecordingWillStop,
    CallTranscriptionOn,
    CallTranscriptionOff,
    HandRaised,
    CallVideoStarted,
}

export type TuneName = keyof typeof Tune;

interface TuneInfo { vibration: number[], sound?: string }

const ClickTuneSelector = 'button, [role="button"], [role="switch"], [role="radio"], [data-tune]';
const DisabledSelector = ':disabled, [aria-disabled="true"], [data-input-disabled="True"]';

const cooldownMap = new Map<Tune, number>([
    [Tune.CancelReply, 1],
    [Tune.OpenModal, 1],
    [Tune.CloseModal, 1],
    [Tune.SelectNavbarItem, 1],
    [Tune.ShowInputError, 1],
    [Tune.BeginRecording, 1],
    [Tune.ConfirmRecording, 1],
    [Tune.EndRecording, 1],
    [Tune.RemindOfRecording, 1],
    [Tune.RecordingWillStop, 1],
    [Tune.StartListening, 1],
    [Tune.StopListening, 1],
    [Tune.StartReplay, 1],
    [Tune.StopReplay, 1],
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
    private static blazorRef: DotNet.DotNetObject;
    private static tunes: Record<Tune, TuneInfo>;
    private static isNative: boolean;

    /** Called by blazor */
    public static init(blazorRef: DotNet.DotNetObject, tunes: Record<Tune, TuneInfo>, isNative: boolean) {
        this.blazorRef = blazorRef;
        this.tunes = tunes;
        this.isNative = isNative;
        this.whenReady.resolve(null);
    }

    /** The tap haptic a click on target plays, or null. A data-href element's tune is played by
     *  DataHrefGesture instead, once the click survives the long-press suppression. */
    public static getClickTune(target: EventTarget | null): Tune | null {
        // A <label>-wrapped checkbox/radio (Toggle, FormRadio) fires a second click on the control
        // itself; skip that synthetic one so one tap plays one haptic, not two.
        if (target instanceof HTMLInputElement && (target.type === 'checkbox' || target.type === 'radio'))
            return null;
        if (!(target instanceof Element))
            return null;

        const element = target.closest<HTMLElement>(ClickTuneSelector);
        if (!element)
            return null;
        if (element.hasAttribute('data-href') && element.hasAttribute('data-tune'))
            return null;
        if (element.matches(DisabledSelector))
            return null;

        const defaultTune = element.matches('[role="switch"], [role="radio"]') ? Tune.ChangeToggle : Tune.ClickButton;
        return this.getElementTune(element, defaultTune);
    }

    /** The tune element's data-tune names, or defaultTune when it has none; "none" and "false" mute it. */
    public static getElementTune(element: HTMLOrSVGElement, defaultTune: Tune | null): Tune | null {
        const tuneName = element.dataset.tune;
        if (tuneName === undefined)
            return defaultTune;

        // Tune also maps numbers back to names, so only a number is a tune
        const tune: unknown = Tune[tuneName as TuneName];
        if (typeof tune !== 'number') {
            if (tuneName !== 'none' && tuneName !== 'false')
                warnLog?.log(`${logScope}.getElementTune: unknown data-tune '${tuneName}'.`);
            return null;
        }

        return tune as Tune === Tune.None ? null : tune as Tune;
    }

    /** Called by blazor */
    public static play(tune: Tune): void {
        void this.playAndWait(tune);
    }

    /** Called by blazor */
    public static async playAndWait(tune: Tune): Promise<void> {
        try {
            await this.whenReady;
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            const tuneInfo = this.tunes[tune] ?? this.tunes[Tune[tune]];
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            if (!tuneInfo) {
                errorLog?.log(`${logScope}.playAndWait: unexpected tune ${tune}.`)
                return;
            }

            if (this.isNative) {
                await this.blazorRef.invokeMethodAsync('play', tune);
                return;
            }

            await Promise.all([this.playVibration(tune, tuneInfo), this.playSound(tune, tuneInfo)]);
        } catch (e) {
            warnLog?.log('Failed yo play tune', tune, e);
        }
    }

    // Private methods

    private static async playVibration(tune: Tune, tuneInfo: TuneInfo): Promise<void> {
        if (tuneInfo.vibration.length == 0)
            return;

        debugLog?.log(`playVibration: '${tune}'`);
        for (let i = 0; i < tuneInfo.vibration.length; i++) {
            const durationMs = tuneInfo.vibration[i];
            if (i % 2 == 0)
                await this.vibrateAndWait(durationMs);
            else
                await delayAsync(durationMs);
        }
    }

    private static async playSound(tune: Tune, tuneInfo: TuneInfo): Promise<void> {
        if (!tuneInfo.sound)
            return;

        debugLog?.log(`playSound: '${tune}'`);
        const ext = DeviceInfo.isWebKit ? '.m4a' : '.webm'; // TODO: allow webm for iOS >= 16.5
        const soundUrl = `dist/sounds/${tuneInfo.sound}${ext}`;
        const cooldown = cooldownMap.get(tune);
        await SoundPlayer.instance.play(soundUrl, cooldown);
    }

    private static vibrate(durationMs = 20): void {
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
