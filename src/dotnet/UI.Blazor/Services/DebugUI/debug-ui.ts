import { PromiseSource } from 'promises';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'DebugUI';
const log = Log.get(LogScope, LogLevel.Info);

export class DebugUI {
    private static backendRef: DotNet.DotNetObject = null;

    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static init(backendRef1: DotNet.DotNetObject): void {
        log?.log(`init`);
        this.backendRef = backendRef1;
        this.whenReady.resolve(undefined);
        globalThis["debugUI"] = this;
    }

    public static startFusionMonitor(): void {
        this.backendRef.invokeMethodAsync('OnStartFusionMonitor');
    };

    public static startTaskMonitor(): void {
        this.backendRef.invokeMethodAsync('OnStartTaskMonitor');
    };

    public static redirect(url: string): void {
        this.backendRef.invokeMethodAsync('OnRedirect', url);
    };
}
