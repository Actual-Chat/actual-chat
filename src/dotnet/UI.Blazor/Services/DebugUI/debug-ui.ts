import { PromiseSource } from 'promises';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'DebugUI';
const log = Log.get(LogScope, LogLevel.Info);

export class DebugUI {
    private static backendRef: DotNet.DotNetObject = null;

    public static init(backendRef1: DotNet.DotNetObject): void {
        log?.log(`init`);
        this.backendRef = backendRef1;
        globalThis["debugUI"] = this;
    }

    public static startFusionMonitor(): void {
        this.backendRef.invokeMethodAsync('StartFusionMonitor');
    };

    public static startTaskMonitor(): void {
        this.backendRef.invokeMethodAsync('StartTaskMonitor');
    };

    public static async getThreadPoolSettings(): Promise<string> {
        const settings = await this.backendRef.invokeMethodAsync('GetThreadPoolSettings');
        console.log(settings);
        return settings as string;
    };

    public static changeThreadPoolSettings(min: number, minIO: number, max: number, maxIO: number): Promise<string> {
        this.backendRef.invokeMethodAsync('ChangeThreadPoolSettings', min, minIO, max, maxIO);
        return this.getThreadPoolSettings();
    };

    public static navigateTo(url: string): void {
        this.backendRef.invokeMethodAsync('NavigateTo', url);
    };
}
