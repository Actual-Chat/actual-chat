import { type Disposable, Disposables } from 'disposable';
import { EventHandler } from 'event-handling';
import { getLogs } from 'logging';
import { rpcNoWait, type RpcNoWait } from 'rpc';
import { SharedSettings, type SharedSettingsSnapshot } from 'shared-settings';

const { warnLog } = getLogs('SharedSettingsWorkerSync');

export interface SharedSettingsWorker {
    updateSharedSettings(settings: SharedSettingsSnapshot, noWait?: RpcNoWait): Promise<void>;
}

export const sharedSettingsWorker: SharedSettingsWorker = {
    updateSharedSettings: async (settings: SharedSettingsSnapshot, _noWait?: RpcNoWait): Promise<void> => {
        SharedSettings.update(settings);
    },
};

export class SharedSettingsWorkerSync {
    private static readonly targets = new Set<SharedSettingsWorker>();
    private static changedHandler: EventHandler<SharedSettingsSnapshot> | null = null;

    public static register(target: SharedSettingsWorker): Disposable {
        this.targets.add(target);
        this.ensureSubscribed();
        this.push(target, SharedSettings.current);
        return Disposables.fromAction(() => {
            this.targets.delete(target);
            if (this.targets.size === 0) {
                this.changedHandler?.dispose();
                this.changedHandler = null;
            }
        });
    }

    private static ensureSubscribed(): void {
        this.changedHandler ??= SharedSettings.changed.add(settings => {
            for (const target of this.targets)
                this.push(target, settings);
        });
    }

    private static push(target: SharedSettingsWorker, settings: SharedSettingsSnapshot): void {
        target.updateSharedSettings(settings, rpcNoWait)
            .catch(error => warnLog?.log('updateSharedSettings failed:', error));
    }
}
