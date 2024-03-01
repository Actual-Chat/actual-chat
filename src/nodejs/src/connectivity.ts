import { delayAsync } from 'promises';
import { Log } from 'logging';

const { infoLog, warnLog } = Log.get('connectivity');

export async function reloadCurrentPage(waitWhenOnline = true): Promise<void> {
    warnLog?.log('reload: reloading...');
    // eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
    await window['opusMediaRecorder']?.stop();

    if (waitWhenOnline)
        await whenOnline();

    if (!window.location.hash) {
        // Refresh with GET
        // noinspection SillyAssignmentJS
        window.location.href = window.location.href;
    } else {
        window.location.reload();
    }
}

export async function whenOnline(checkInterval = 2000): Promise<void> {
    let wasOnline = true;
    // eslint-disable-next-line no-constant-condition
    while (true) {
        if (await isOnline()) {
            // Second check - just in case
            await delayAsync(250);
            if (await isOnline())
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

export async function isOnline(): Promise<boolean> {
    if (isMauiApp())
        return true;

    try {
        const response = await fetch('/favicon.ico', { cache: 'no-store' });
        if (response.ok)
            return true;
    }
    catch {
        // Intended
    }
    return false;
}

// Private declarations

let _isMauiApp: boolean = null;

function isMauiApp(): boolean {
    if (_isMauiApp === null)
        _isMauiApp = document.body.classList.contains('app-maui')
    return _isMauiApp;
}
