import { delayAsync, PromiseSource } from 'promises';
import { Log } from 'logging';
import { OnDeviceAwake } from 'on-device-awake';

const { infoLog, warnLog } = Log.get('Connectivity');

export class Connectivity {
    public static async reloadCurrentPage(waitWhenOnline = true): Promise<void> {
        warnLog?.log('reload: reloading...');
        // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
        await window['opusMediaRecorder']?.stop();

        if (waitWhenOnline)
            await this.whenOnline();

        window.location.reload();
        /*
        if (!window.location.hash) {
            // Refresh with GET
            // noinspection SillyAssignmentJS
            window.location.href = window.location.href;
        } else {
            window.location.reload();
        }
        */
    }

    public static async whenOnline(checkInterval = 2000): Promise<void> {
        let wasOnline = true;
        // eslint-disable-next-line no-constant-condition
        while (true) {
            if (await this.isOnline()) {
                // Second check - just in case
                await delayAsync(250);
                if (await this.isOnline())
                    break;
            }

            if (wasOnline) {
                wasOnline = false;
                warnLog?.log(`whenOnline: offline`);
            }
            await delayAsync(checkInterval);
        }
        if (!wasOnline)
            infoLog?.log(`whenOnline: online`);
    }

    public static async isOnline(): Promise<boolean> {
        if (isMauiApp())
            return true;

        const whenWokeUp = new PromiseSource<null>();
        const handler = OnDeviceAwake.events.add(() => whenWokeUp.resolve(null))
        try {
            const firstCheck = check();
            if (await Promise.race([firstCheck, whenWokeUp]) === null)
                return Promise.race([firstCheck, check(50)])
            return await firstCheck;
        }
        finally {
            handler.dispose();
            whenWokeUp.resolve(null);
        }

        async function check(delayMs = 0): Promise<boolean> {
            try {
                if (delayMs > 0)
                    await delayAsync(delayMs);
                const response = await fetch('/favicon.ico', { cache: 'no-store' });
                if (response.ok)
                    return true;
            }
            catch {
                // Intended
            }
            return false;
        }
    }
}

// Private declarations

let _isMauiApp: boolean = null;

function isMauiApp(): boolean {
    if (_isMauiApp === null)
        _isMauiApp = document.body.classList.contains('app-maui')
    return _isMauiApp;
}
