import { EventHandlerSet } from 'event-handling';
import { ServerClock } from 'server-clock';

export interface SharedSettingsSnapshot {
    serverClockOffsetMs: number;
    apiUrl?: string;
}

let current: SharedSettingsSnapshot = {
    serverClockOffsetMs: ServerClock.offsetMs,
};

function clone(settings: SharedSettingsSnapshot): SharedSettingsSnapshot {
    return { ...settings };
}

function applyToLocalRealm(settings: SharedSettingsSnapshot): void {
    ServerClock.updateOffset(Date.now() + settings.serverClockOffsetMs);
}

export class SharedSettings {
    public static readonly changed = new EventHandlerSet<SharedSettingsSnapshot>();

    public static get current(): SharedSettingsSnapshot {
        return clone(current);
    }

    public static update(settings: Partial<SharedSettingsSnapshot>): void {
        current = { ...current, ...settings };
        applyToLocalRealm(current);
        SharedSettings.changed.trigger(SharedSettings.current);
    }

    public static updateServerClockOffset(serverNowMs: number): void {
        SharedSettings.update({ serverClockOffsetMs: serverNowMs - Date.now() });
    }
}
