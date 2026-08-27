import { getLogs } from 'logging';

const { debugLog, warnLog } = getLogs('PrefetchUI');

// The JS half of PrefetchUI.
export class PrefetchUI {
    private static backendRef: DotNet.DotNetObject | null = null;

    public static init(backendRef1: DotNet.DotNetObject): void {
        this.backendRef = backendRef1;
    }

    // NOTE: a failed prefetch is logged and nothing else - never an unhandled rejection
    public static request(prefetchRef: string): void {
        if (this.backendRef === null)
            return;

        debugLog?.log(`request:`, prefetchRef);
        this.backendRef.invokeMethodAsync('OnPrefetchRequest', prefetchRef)
            .catch((e: unknown) => warnLog?.log(`request: failed`, e));
    }
}
