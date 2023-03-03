import { Interactive } from 'interactive';
import { default as NoSleep } from '@uriopass/nosleep.js';
import { Log, LogLevel, LogScope } from 'logging';
import { DocumentEvents } from 'event-handling';
import { filter, exhaustMap, tap } from 'rxjs';
import { getOrInheritData } from 'dom-helpers';
import { BrowserInfo } from '../BrowserInfo/browser-info';
import { DeviceInfo } from 'device-info';

const LogScope: LogScope = 'KeepAwakeUI';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const infoLog = Log.get(LogScope, LogLevel.Info);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const noSleep = new NoSleep();

export class KeepAwakeUI {
    private static _mustKeepAwake: boolean;
    private static isSubscribedOnClick = false;

    public static setKeepAwake(mustKeepAwake: boolean) {
        debugLog?.log(`setKeepAwake(${mustKeepAwake})`);
        this._mustKeepAwake = mustKeepAwake;
        if (mustKeepAwake) {
            return this.enableNoSleep();
        } else {
            this.disableNoSleep();
        }
    };

    public static warmup() {
        debugLog?.log(`warmup`);
        return this.enableNoSleep().then(() => {
                if (!this._mustKeepAwake) {
                    noSleep.disable();
                }
            })
            .catch(e => errorLog?.log(`warmup: error:`, e));
    }

    /*
    * Workaround for iOS safari
    * */
    public static subscribeOnKeepAwake() {
        if (!DeviceInfo.isIos || BrowserInfo.appKind === 'WasmApp')
            return;

        if (this.isSubscribedOnClick)
            return;

        debugLog?.log(`subscribeOnKeepAwake`);
        DocumentEvents.active.click$
            .pipe(
                filter(ev => {
                    const [triggerElement, mustKeepAwake] = getOrInheritData(ev.target, 'mustKeepAwake');
                    return triggerElement !== null && mustKeepAwake.toLowerCase() === 'true';
                }),
                tap(() => debugLog?.log(`subscribeOnKeepAwake: preventive enableNoSleep`)),
                exhaustMap(() => this.enableNoSleep()),
            ).subscribe();
        this.isSubscribedOnClick = true;
    }

    private static async enableNoSleep(): Promise<void> {
        if (noSleep.isEnabled)
            return;
        infoLog?.log(`enableNoSleep`);
        return noSleep.enable().catch(e => errorLog?.log(`enableNoSleep: error:`, e));
    }

    private static disableNoSleep() {
        if (!noSleep.isEnabled)
            return;
        infoLog?.log(`disableNoSleep`);
        try {
            noSleep.disable();
        } catch (e) {
            errorLog?.log(`disableNoSleep: error:`, e);
        }
    }
}

Interactive.whenInteractive().then(() => KeepAwakeUI.warmup());
KeepAwakeUI.subscribeOnKeepAwake();
