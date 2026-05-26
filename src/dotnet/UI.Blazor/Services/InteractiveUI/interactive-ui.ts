import { delayAsync } from 'actuallab-core';
import { Interactive } from 'interactive';
import { getLogs } from 'logging';

const { debugLog, errorLog } = getLogs('InteractiveUI');

export class InteractiveUI {
    private static backendRef: DotNet.DotNetObject = null!;
    private static _backendIsInteractive = false;

    public static init(backendRef: DotNet.DotNetObject) {
        debugLog?.log(`init`);
        this.backendRef = backendRef;
        Interactive.isInteractiveChanged.add(() => this.sync());
        // sync if is already interactive
        if (Interactive.isInteractive) void this.sync();
    }

    public static async demand(operation: string): Promise<void> {
        try {
            debugLog?.log(`demand: operation = '${operation}'`);
            await this.backendRef.invokeMethodAsync('Demand', operation);
        } catch (error) {
            errorLog?.log(`demand: failed, error:`, error);
        }
    }

    // Private methods

    private static _isSyncing: boolean;
    private static async sync(): Promise<void> {
        if (this._isSyncing) return; // Running sync will do the job anyway - it loops while there is any diff

        this._isSyncing = true;
        for (;;) {
            const isInteractive = Interactive.isInteractive;
            if (isInteractive == this._backendIsInteractive) break;

            try {
                debugLog?.log(`sync: calling IsInteractiveChanged(${isInteractive}) on backend`);
                await this.backendRef.invokeMethodAsync('IsInteractiveChanged', isInteractive);
                this._backendIsInteractive = isInteractive;
            } catch (error) {
                errorLog?.log(`sync: failed to reach the backend, error:`, error);
                await delayAsync(1000);
            }
        }
        this._isSyncing = false;
    }
}
