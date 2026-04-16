import { NoSleep } from './nosleep/nosleep';
import { getLogs } from 'logging';
import { DocumentEvents } from 'event-handling';
import { filter, exhaustMap, tap, concatMap } from 'rxjs';
import { getOrInheritData } from 'dom-helpers';
import { BrowserInfo } from '../BrowserInfo/browser-info';
import { DeviceInfo } from 'device-info';

const { debugLog, errorLog } = getLogs('KeepAwakeUI');

const noSleep = new NoSleep();

export class KeepAwakeUI {
    private static mustKeepAwake: boolean;
    private static isSubscribedOnClick = false;

    /** Called by Blazor */
    public static async setKeepAwake(mustKeepAwake: boolean) {
        debugLog?.log(`setKeepAwake(${mustKeepAwake})`);
        this.mustKeepAwake = mustKeepAwake;
        if (mustKeepAwake) {
            return this.enableNoSleep();
        } else {
            await this.disableNoSleep();
        }
    };

    private static warmup() {
        debugLog?.log('-> warmup()');
        return this.enableNoSleep().then(async () => {
            if (!this.mustKeepAwake) {
                debugLog?.log('warmup: disabling since mustKeepAwake=', this.mustKeepAwake)
                await this.disableNoSleep();
            }
        })
            .catch((e: unknown) => errorLog?.log('warmup: error:', e))
            .finally(() => debugLog?.log('<- warmup()'));
    }

    /*
    * Workaround for safari < 16.4
    * */
    public static async subscribeOnKeepAwakeTriggers() {
        if (noSleep.isNativeWakeLockSupported)
            return;
        await BrowserInfo.whenReady;
        const isSsbSafari = BrowserInfo.hostKind === 'WebServer' && DeviceInfo.isWebKit;
        if (!isSsbSafari)
            return;

        if (this.isSubscribedOnClick)
            return;

        debugLog?.log('subscribeOnKeepAwake');
        const subscription = DocumentEvents.active.click$
            .pipe(
                filter(ev => {
                    const [triggerElement, mustKeepAwake] = getOrInheritData(ev.target, 'mustKeepAwake');
                    return triggerElement !== null && mustKeepAwake?.toLowerCase() === 'true';
                }),
                tap(() => debugLog?.log(`subscribeOnKeepAwake: preventive enableNoSleep`)),
                exhaustMap(() => this.enableNoSleep()),
            ).subscribe();
        this.isSubscribedOnClick = true;
        // eslint-disable-next-line @typescript-eslint/await-thenable
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
        const isSsbSafari = BrowserInfo.hostKind === 'WebServer' && DeviceInfo.isWebKit;
        if (!isSsbSafari)
            return;

        // eslint-disable-next-line
        await DocumentEvents.active.visibilityChange$
            .pipe(concatMap(async () => {
                if (document.visibilityState == 'hidden')
                    await this.disableNoSleep();
                else if (this.mustKeepAwake)
                    return this.enableNoSleep();
            })).subscribe();
    }

    public static async subscribeOnFirstInteraction() {
        await BrowserInfo.whenReady;
        if (BrowserInfo.hostKind === 'MauiApp')
            return;
        // TODO: find out what's wrong with Interactive - why it breaks user gesture context in safari
        document.body.addEventListener(
            'click',
            () => void this.warmup(),
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
            .catch((e: unknown) => errorLog?.log('enableNoSleep: error:', e))
            .finally(() => debugLog?.log('<- enableNoSleep()'));
    }

    private static async disableNoSleep() {
        debugLog?.log('-> disableNoSleep()');
        try {
            if (!noSleep.isEnabled) {
                debugLog?.log('<- disableNoSleep(): already disabled');
                return;
            }

            await noSleep.disable();
            debugLog?.log('disableNoSleep: success');
        } catch (e) {
            errorLog?.log('disableNoSleep: error:', e);
        }
        finally {
            debugLog?.log   ('<- disableNoSleep()');
        }
    }
}

void KeepAwakeUI.subscribeOnFirstInteraction().then();
void KeepAwakeUI.subscribeOnKeepAwakeTriggers().then();
void KeepAwakeUI.subscribeOnDocumentVisibility().then();
