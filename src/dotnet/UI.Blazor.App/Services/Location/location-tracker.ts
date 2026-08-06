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
