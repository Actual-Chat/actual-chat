import { delayAsync } from 'promises';
import { Interactive } from 'interactive';
import { Log } from 'logging';

const { debugLog, errorLog } = Log.get('InteractiveUI');

export class InteractiveUI {
    private static backendRef: DotNet.DotNetObject = null;
    private static _backendIsInteractive = false;

    public static init(backendRef: DotNet.DotNetObject) {
        debugLog?.log(`init`);
        this.backendRef = backendRef;
        Interactive.isInteractiveChanged.add(() => this.sync());
        // sync if is already interactive
        if (Interactive.isInteractive)
            void this.sync();
    }

    // Private methods

    private static _isSyncing: boolean;
    private static async sync(): Promise<void> {
        if (this._isSyncing)
            return; // Running sync will do the job anyway - it loops while there is any diff

        this._isSyncing = true;
        for (;;) {
            const isInteractive = Interactive.isInteractive;
            if (isInteractive == this._backendIsInteractive)
                break;

            try {
                debugLog?.log(`sync: calling IsInteractiveChanged(${isInteractive}) on backend`);
                await this.backendRef.invokeMethodAsync("IsInteractiveChanged", isInteractive);
                this._backendIsInteractive = isInteractive;
            }
            catch (error) {
                errorLog?.log(`sync: failed to reach the backend, error:`, error);
                await delayAsync(1000);
            }
        }
        this._isSyncing = false;
    }
}
