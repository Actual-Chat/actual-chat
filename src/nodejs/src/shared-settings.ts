import { EventHandlerSet } from 'event-handling';
import { AC, initAppConstants, type AppConstants } from 'app-constants';
import { ServerClock } from 'clocks';

export interface SharedSettingsSnapshot {
    serverClockOffsetMs: number;
    // Bumped on a confirmed clock step, never on a slewed refinement — holders of
    // absolute anchors rebase when it changes.
    serverClockEpoch: number;
    apiUrl?: string;
    appConstants?: AppConstants;
    // Mirrors Api.getSessionToken() from the main thread; workers pass it as the `?s=`
    // query parameter when dialing the RPC WebSocket, avoiding a per-request round trip.
    sessionToken?: string;
    // screen.orientation.angle from the main thread: degrees CW from natural portrait,
    // one of {0, 90, 180, 270}.
    screenOrientation?: number;
    // Device-pose quarter-turn (0..3, CW), set by DeviceOrientation and overridable via
    // debugUI.setDeviceOrientation.
    deviceOrientation?: number;
    // Device-pose angle, degrees CW from natural portrait, quantized to 10-degree steps
    // to avoid excessive worker updates.
    deviceOrientationAngle?: number;
    // BrowserInfo.hostKind / appKind. Mirrored here because workers have no
    // BrowserInfo: codec policy needs to know whether this is the MAUI app and
    // on which platform, and workers are where the encoder actually runs.
    hostKind?: string;
    appKind?: string;
}

let current: SharedSettingsSnapshot = {
    serverClockOffsetMs: ServerClock.offsetMs,
    serverClockEpoch: ServerClock.epoch,
};
let appConstants: AppConstants | undefined;

function clone(settings: SharedSettingsSnapshot): SharedSettingsSnapshot {
    return { ...settings };
}

function applyToLocalRealm(settings: SharedSettingsSnapshot): void {
    ServerClock.updateOffset(Date.now() + settings.serverClockOffsetMs, settings.serverClockEpoch);
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

    // C# always pushes a Date-relative offset, never an absolute server timestamp:
    // an absolute would be converted against Date.now() at APPLY time, so a delayed
    // interop execution (doze, frozen WebView) would skew the clock by that delay.
    public static setServerClockOffsetMs(offsetMs: number, epoch: number): void {
        SharedSettings.update({ serverClockOffsetMs: offsetMs, serverClockEpoch: epoch });
    }

    public static getClientTimeMs(): number {
        return Date.now();
    }
}
