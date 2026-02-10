import { Log } from 'logging';

const { infoLog } = Log.get('DebugUI');

export class DebugUI {
    private static backendRef: DotNet.DotNetObject = null!;

    public static init(backendRef1: DotNet.DotNetObject): void {
        infoLog?.log(`init`);
        this.backendRef = backendRef1;
        globalThis.debugUI = this;
    }

    public static startFusionMonitor(): void {
        void this.backendRef.invokeMethodAsync('StartFusionMonitor');
    };

    public static startTaskMonitor(): void {
        void this.backendRef.invokeMethodAsync('StartTaskMonitor');
    };

    public static async getThreadPoolSettings(): Promise<string> {
        const settings = await this.backendRef.invokeMethodAsync('GetThreadPoolSettings');
        console.log(settings);
        return settings as string;
    };

    public static async changeThreadPoolSettings(min: number, minIO: number, max: number, maxIO: number): Promise<string> {
        await this.backendRef.invokeMethodAsync('ChangeThreadPoolSettings', min, minIO, max, maxIO);
        return await this.getThreadPoolSettings();
    };

    public static navigateTo(url: string): void {
        void this.backendRef.invokeMethodAsync('NavigateTo', url);
    };

    public static disconnectRpc(): void {
        void this.backendRef.invokeMethodAsync('DisconnectRpc');
    };

    public static resetOnboarding(enable: boolean): void {
        void this.backendRef.invokeMethodAsync('ResetOnboarding', enable);
    };

    public static resetBubbles(enable: boolean): void {
        void this.backendRef.invokeMethodAsync('ResetBubbles', enable);
    };
}
