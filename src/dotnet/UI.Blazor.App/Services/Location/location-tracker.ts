import { getLogs } from 'logging';

const { warnLog } = getLogs('LocationTracker');

// Duplicated by intent from .NET Constants.Location.GetTimeout (kept in sync manually).
const getTimeoutMs = 15_000;

interface GeoFix {
    latitude: number;
    longitude: number;
    accuracy: number | null;
    bearing: number | null;
    // Epoch ms of the fix itself - with maximumAge > 0 the browser may return a much older one.
    timestamp: number;
}

export class LocationTracker {
    private readonly watchId: number;

    public static start(blazorRef: DotNet.DotNetObject): LocationTracker {
        return new LocationTracker(blazorRef);
    }

    public static getCurrent(mustBeFresh: boolean): Promise<GeoFix | null> {
        return new Promise(resolve => {
            navigator.geolocation.getCurrentPosition(
                position => {
                    const c = position.coords;
                    resolve({
                        latitude: c.latitude, longitude: c.longitude, accuracy: c.accuracy, bearing: c.heading,
                        timestamp: position.timestamp,
                    });
                },
                error => { warnLog?.log('getCurrent error', error); resolve(null); },
                { enableHighAccuracy: true, maximumAge: mustBeFresh ? 0 : Infinity, timeout: getTimeoutMs });
        });
    }

    constructor(blazorRef: DotNet.DotNetObject) {
        this.watchId = navigator.geolocation.watchPosition(
            position => {
                const c = position.coords;
                void blazorRef.invokeMethodAsync(
                    'OnLocation', c.latitude, c.longitude, c.accuracy, c.heading, position.timestamp);
            },
            error => {
                warnLog?.log('geolocation error', error);
                void blazorRef.invokeMethodAsync('OnError', error.code);
            },
            { enableHighAccuracy: true, maximumAge: 0 });
    }

    public stop(): void {
        navigator.geolocation.clearWatch(this.watchId);
    }
}

// Duplicated by intent from .NET LocationTrackerBase.MinHeadingChange (kept in sync manually).
const minHeadingChange = 5;

interface CompassOrientationEvent extends DeviceOrientationEvent {
    /** Safari only: degrees clockwise from magnetic north, already absolute. */
    webkitCompassHeading?: number;
}

interface OrientationPermissionApi {
    requestPermission?: () => Promise<PermissionState>;
}

/**
 * Streams the compass heading of the screen's top edge to .NET via `OnHeading`.
 * A page-level singleton: unlike a geolocation watch there's only ever one, so
 * .NET starts and stops it by name and needs no handle to hold on to.
 */
export class HeadingTracker {
    private static current: HeadingTracker | null = null;

    private readonly eventName: 'deviceorientationabsolute' | 'deviceorientation';
    private lastHeading: number | null = null;

    public static start(blazorRef: DotNet.DotNetObject): void {
        HeadingTracker.stop();
        HeadingTracker.current = new HeadingTracker(blazorRef);
    }

    public static stop(): void {
        HeadingTracker.current?.dispose();
        HeadingTracker.current = null;
    }

    private constructor(private readonly blazorRef: DotNet.DotNetObject) {
        // Chrome's plain deviceorientation is relative to wherever the page loaded, so only the
        // absolute event is a compass there; Safari has no such event but adds webkitCompassHeading.
        this.eventName = 'ondeviceorientationabsolute' in window ? 'deviceorientationabsolute' : 'deviceorientation';
        window.addEventListener(this.eventName, this.onOrientation);
        // Safari requires a user gesture for this, so it may well be rejected: the heading is then just absent.
        const permissionApi = DeviceOrientationEvent as unknown as OrientationPermissionApi;
        permissionApi.requestPermission?.().catch((e: unknown) => warnLog?.log('requestPermission error', e));
    }

    // Private methods

    private dispose(): void {
        window.removeEventListener(this.eventName, this.onOrientation);
    }

    private readonly onOrientation = (event: Event): void => {
        const heading = HeadingTracker.getHeading(event as CompassOrientationEvent);
        if (heading == null)
            return;

        if (this.lastHeading != null && HeadingTracker.getAngleDistance(heading, this.lastHeading) < minHeadingChange)
            return;

        this.lastHeading = heading;
        void this.blazorRef.invokeMethodAsync('OnHeading', heading);
    };

    private static getHeading(event: CompassOrientationEvent): number | null {
        let deviceHeading: number;
        if (event.webkitCompassHeading != null)
            deviceHeading = event.webkitCompassHeading;
        else if (event.absolute && event.alpha != null)
            deviceHeading = 360 - event.alpha;
        else
            return null;

        // Safari before 16.4 has no screen.orientation
        const screenOrientation = screen.orientation as ScreenOrientation | undefined;
        const screenAngle = screenOrientation?.angle ?? 0;
        return (deviceHeading + screenAngle) % 360;
    }

    private static getAngleDistance(a: number, b: number): number {
        const distance = Math.abs(a - b) % 360;
        return distance > 180 ? 360 - distance : distance;
    }
}
