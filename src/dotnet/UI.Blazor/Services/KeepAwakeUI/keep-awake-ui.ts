import { NoSleep } from './nosleep/nosleep';
import { Log } from 'logging';
import { DocumentEvents } from 'event-handling';
import { filter, exhaustMap, tap, concatMap } from 'rxjs';
import { getOrInheritData } from 'dom-helpers';
import { BrowserInfo } from '../BrowserInfo/browser-info';
import { DeviceInfo } from 'device-info';

const { debugLog, errorLog } = Log.get('KeepAwakeUI');

const noSleep = new NoSleep();

export class KeepAwakeUI {
    private static mustKeepAwake: boolean;
    private static isSubscribedOnClick = false;

    public static setKeepAwake(mustKeepAwake: boolean) {
        debugLog?.log(`setKeepAwake(${mustKeepAwake})`);
        this.mustKeepAwake = mustKeepAwake;
        if (mustKeepAwake) {
            return this.enableNoSleep();
        } else {
            this.disableNoSleep();
        }
    };

    private static warmup() {
        debugLog?.log('-> warmup()');
        return this.enableNoSleep().then(() => {
                if (!this.mustKeepAwake) {
                    debugLog?.log("warmup: disabling since mustKeepAwake=", this.mustKeepAwake)
                    this.disableNoSleep();
                }
            })
            .catch(e => errorLog?.log('warmup: error:', e))
            .finally(() => debugLog?.log('<- warmup()'));
    }

    /*
    * Workaround for safari < 16.4
    * */
    public static async subscribeOnKeepAwakeTriggers() {
        if (noSleep.isNativeWakeLockSupported)
            return;
        await BrowserInfo.whenReady;
        const isSsbSafari = BrowserInfo.appKind === 'WebServer' && DeviceInfo.isWebKit;
        if (!isSsbSafari)
            return;

        if (this.isSubscribedOnClick)
            return;

        debugLog?.log('subscribeOnKeepAwake');
        const subscription = DocumentEvents.active.click$
            .pipe(
                filter(ev => {
                    const [triggerElement, mustKeepAwake] = getOrInheritData(ev.target, 'mustKeepAwake');
                    return triggerElement !== null && mustKeepAwake.toLowerCase() === 'true';
                }),
                tap(() => debugLog?.log(`subscribeOnKeepAwake: preventive enableNoSleep`)),
                exhaustMap(() => this.enableNoSleep()),
            ).subscribe();
        this.isSubscribedOnClick = true;
        await subscription;
    }

    /*
    * Workaround for iOS safari
    * Fixes issue when hiding browser or switching between apps or force sleep breaks keep awake functionality
    * */
    public static async subscribeOnDocumentVisibility() {
        if (noSleep.isNativeWakeLockSupported)
            return;
        await BrowserInfo.whenReady;
        const isSsbSafari = BrowserInfo.appKind === 'WebServer' && DeviceInfo.isWebKit;
        if (!isSsbSafari)
            return;

        await DocumentEvents.active.visibilityChange$
            .pipe(concatMap(async () => {
                if (document.visibilityState == 'hidden')
                    this.disableNoSleep();
                else if (this.mustKeepAwake)
                    return this.enableNoSleep();
            })).subscribe();
    }

    public static async subscribeOnFirstInteraction() {
        await BrowserInfo.whenReady;
        if (BrowserInfo.appKind === 'MauiApp')
            return;
        // TODO: find out what's wrong with Interactive - why it breaks user gesture context in safari
        document.body.addEventListener(
            'click',
            () => this.warmup(),
            { capture: true, passive: true, once: true });
    }

    private static async enableNoSleep(): Promise<void> {
        debugLog?.log(`-> enableNoSleep()`);
        if (noSleep.isEnabled) {
            debugLog?.log(`<- enableNoSleep(): already enabled`);
            return;
        }

        return noSleep.enable()
            .then(() => debugLog?.log('enableNoSleep: success'))
            .catch(e => errorLog?.log('enableNoSleep: error:', e))
            .finally(() => debugLog?.log('<- enableNoSleep()'));
    }

    private static disableNoSleep() {
        debugLog?.log('-> disableNoSleep()');
        try {
            if (!noSleep.isEnabled) {
                debugLog?.log('<- disableNoSleep(): already disabled');
                return;
            }

            noSleep.disable();
            debugLog?.log('disableNoSleep: success');
        } catch (e) {
            errorLog?.log('disableNoSleep: error:', e);
        }
        finally {
            debugLog?.log   ('<- disableNoSleep()');
        }
    }
}

KeepAwakeUI.subscribeOnFirstInteraction().then();
KeepAwakeUI.subscribeOnKeepAwakeTriggers().then();
KeepAwakeUI.subscribeOnDocumentVisibility().then();
