import { getLogs } from 'logging';

const { warnLog } = getLogs('LocationTracker');

export class LocationTracker {
    private readonly watchId: number;

    public static start(blazorRef: DotNet.DotNetObject): LocationTracker {
        return new LocationTracker(blazorRef);
    }

    constructor(blazorRef: DotNet.DotNetObject) {
        this.watchId = navigator.geolocation.watchPosition(
            position => {
                const c = position.coords;
                void blazorRef.invokeMethodAsync('OnLocation', c.latitude, c.longitude, c.accuracy, c.heading);
            },
            error => warnLog?.log('geolocation error', error),
            { enableHighAccuracy: true, maximumAge: 0 });
    }

    public stop(): void {
        navigator.geolocation.clearWatch(this.watchId);
    }
}
