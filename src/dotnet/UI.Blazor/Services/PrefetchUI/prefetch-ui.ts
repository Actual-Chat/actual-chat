import { getLogs } from 'logging';

const { debugLog } = getLogs('PrefetchUI');

// The JS half of PrefetchUI.
export class PrefetchUI {
    private static backendRef: DotNet.DotNetObject | null = null;

    public static init(backendRef1: DotNet.DotNetObject): void {
        this.backendRef = backendRef1;
    }

    public static request(prefetchRef: string): void {
        if (this.backendRef === null)
            return;

        debugLog?.log(`request:`, prefetchRef);
        void this.backendRef.invokeMethodAsync('OnPrefetchRequest', prefetchRef);
    }
}
