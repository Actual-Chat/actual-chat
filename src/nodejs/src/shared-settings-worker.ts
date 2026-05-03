import { type Disposable, Disposables } from 'disposable';
import { EventHandler } from 'event-handling';
import { getLogs } from 'logging';
import { rpcNoWait, type RpcNoWait } from 'rpc';
import { SharedSettings, type SharedSettingsSnapshot } from 'shared-settings';

const { warnLog } = getLogs('default');

export interface SharedSettingsWorker {
    updateSharedSettings(settings: SharedSettingsSnapshot, noWait?: RpcNoWait): Promise<void>;
}

export const sharedSettingsWorker: SharedSettingsWorker = {
    updateSharedSettings: (settings: SharedSettingsSnapshot, _noWait?: RpcNoWait): Promise<void> => {
        SharedSettings.update(settings);
        return Promise.resolve();
    },
};

export class SharedSettingsWorkerSync {
    private static readonly targets = new Set<SharedSettingsWorker>();
    private static changedHandler: EventHandler<SharedSettingsSnapshot> | null = null;

    public static register(target: SharedSettingsWorker): Disposable {
        this.targets.add(target);
        this.ensureSubscribed();
        this.push(target, SharedSettings.all);
        return Disposables.fromAction(() => {
            this.targets.delete(target);
            if (this.targets.size === 0) {
                this.changedHandler?.dispose();
                this.changedHandler = null;
            }
        });
    }

    private static ensureSubscribed(): void {
        this.changedHandler ??= SharedSettings.changed.add(_settings => {
            for (const target of this.targets)
                this.push(target, SharedSettings.all);
        });
    }

    private static push(target: SharedSettingsWorker, settings: SharedSettingsSnapshot): void {
        target.updateSharedSettings(settings, rpcNoWait)
            .catch((error: unknown) => warnLog?.log('updateSharedSettings failed:', error));
    }
}
