import { EventHandlerSet } from 'event-handling';
import { AC, initAppConstants, type AppConstants } from 'app-constants';
import { ServerClock } from 'server-clock';

export interface SharedSettingsSnapshot {
    serverClockOffsetMs: number;
    apiUrl?: string;
    appConstants?: AppConstants;
}

let current: SharedSettingsSnapshot = {
    serverClockOffsetMs: ServerClock.offsetMs,
};
let appConstants: AppConstants | undefined;

function clone(settings: SharedSettingsSnapshot): SharedSettingsSnapshot {
    return { ...settings };
}

function applyToLocalRealm(settings: SharedSettingsSnapshot): void {
    ServerClock.updateOffset(Date.now() + settings.serverClockOffsetMs);
    if (settings.appConstants) {
        appConstants ??= settings.appConstants;
        initAppConstants(appConstants);
    }
}

function tryGetCurrentAppConstants(): AppConstants | undefined {
    const maybeConstants = AC as Partial<AppConstants>;
    return appConstants ?? (maybeConstants.video && maybeConstants.audio ? AC : undefined);
}

export class SharedSettings {
    public static readonly changed = new EventHandlerSet<SharedSettingsSnapshot>();

    public static get current(): SharedSettingsSnapshot {
        return clone(current);
    }

    public static get all(): SharedSettingsSnapshot {
        const appConstants = tryGetCurrentAppConstants();
        return appConstants ? { ...current, appConstants } : SharedSettings.current;
    }

    public static update(settings: Partial<SharedSettingsSnapshot>): void {
        if (settings.appConstants)
            appConstants ??= settings.appConstants;
        const { appConstants: _appConstants, ...regularSettings } = settings;
        current = { ...current, ...regularSettings };
        const snapshot = SharedSettings.all;
        applyToLocalRealm(snapshot);
        SharedSettings.changed.trigger(SharedSettings.current);
    }

    public static updateServerClockOffset(serverNowMs: number): void {
        SharedSettings.update({ serverClockOffsetMs: serverNowMs - Date.now() });
    }
}
