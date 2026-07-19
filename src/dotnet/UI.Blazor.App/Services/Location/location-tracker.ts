import { getLogs } from 'logging';

const { warnLog } = getLogs('LocationTracker');

// Duplicated by intent from .NET Constants.Location.GetTimeout (kept in sync manually).
const getTimeoutMs = 15_000;

interface GeoPoint {
    latitude: number;
    longitude: number;
    accuracy: number | null;
    bearing: number | null;
}

export class LocationTracker {
    private readonly watchId: number;

    public static start(blazorRef: DotNet.DotNetObject): LocationTracker {
        return new LocationTracker(blazorRef);
    }

    public static getCurrent(force: boolean): Promise<GeoPoint | null> {
        return new Promise(resolve => {
            navigator.geolocation.getCurrentPosition(
                position => {
                    const c = position.coords;
                    resolve({
                        latitude: c.latitude, longitude: c.longitude, accuracy: c.accuracy, bearing: c.heading,
                    });
                },
                error => { warnLog?.log('getCurrent error', error); resolve(null); },
                { enableHighAccuracy: true, maximumAge: force ? 0 : Infinity, timeout: getTimeoutMs });
        });
    }

    constructor(blazorRef: DotNet.DotNetObject) {
        this.watchId = navigator.geolocation.watchPosition(
            position => {
                const c = position.coords;
                void blazorRef.invokeMethodAsync('OnLocation', c.latitude, c.longitude, c.accuracy, c.heading);
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
