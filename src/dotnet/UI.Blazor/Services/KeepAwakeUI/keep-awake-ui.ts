import { NoSleep } from './nosleep/nosleep';
import { getLogs } from 'logging';

const { debugLog, errorLog } = getLogs('KeepAwakeUI');

const noSleep = new NoSleep();

export class KeepAwakeUI {
    /** Called by Blazor */
    public static async setKeepAwake(mustKeepAwake: boolean): Promise<void> {
        debugLog?.log(`setKeepAwake(${mustKeepAwake})`);
        if (mustKeepAwake)
            await this.enableNoSleep();
        else
            await this.disableNoSleep();
    }

    private static async enableNoSleep(): Promise<void> {
        debugLog?.log('-> enableNoSleep()');
        if (noSleep.isEnabled) {
            debugLog?.log('<- enableNoSleep(): already enabled');
            return;
        }

        try {
            await noSleep.enable();
        }
        catch (e) {
            errorLog?.log('enableNoSleep: error:', e);
        }
        finally {
            debugLog?.log('<- enableNoSleep()');
        }
    }

    private static async disableNoSleep(): Promise<void> {
        debugLog?.log('-> disableNoSleep()');
        if (!noSleep.isEnabled) {
            debugLog?.log('<- disableNoSleep(): already disabled');
            return;
        }

        try {
            await noSleep.disable();
        }
        catch (e) {
            errorLog?.log('disableNoSleep: error:', e);
        }
        finally {
            debugLog?.log('<- disableNoSleep()');
        }
    }
}
