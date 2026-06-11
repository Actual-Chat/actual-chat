import { EventHandlerSet } from 'event-handling';
import { AC, initAppConstants, type AppConstants } from 'app-constants';
import { ServerClock } from 'clocks';

export interface SharedSettingsSnapshot {
    serverClockOffsetMs: number;
    apiUrl?: string;
    appConstants?: AppConstants;
    /** Session token for worker-side RPC peer auth. Mirrors what
     *  `Api.getSessionToken()` returns on the main thread. Workers
     *  observe updates via {@link SharedSettings.changed} and pass
     *  this string into the `?s=` query parameter when dialing the
     *  RPC WebSocket — no per-request round-trip needed. */
    sessionToken?: string;
    /** Screen orientation angle (degrees CW from natural portrait,
     *  one of {0, 90, 180, 270}). Mirrors `screen.orientation.angle`
     *  from the main thread. */
    screenOrientation?: number;
    /** Device-pose quarter-turn (0..3, CW). Set by the main thread's
     *  {@link DeviceOrientation}; overridden by `debugUI.setDeviceOrientation`.
     *  Workers read this via the same `DeviceOrientation.current`. */
    deviceOrientation?: number;
    /** Device-pose angle in degrees CW from natural portrait, quantized to
     *  10-degree steps to avoid excessive worker updates. Workers read this
     *  via `DeviceOrientation.angle`. */
    deviceOrientationAngle?: number;
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

    // Server render mode pushes the already-compensated offset directly (the C#
    // side NTP-measures it over the circuit), bypassing the stale serverNow recompute.
    public static setServerClockOffsetMs(offsetMs: number): void {
        SharedSettings.update({ serverClockOffsetMs: offsetMs });
    }

    public static getClientTimeMs(): number {
        return Date.now();
    }
}
