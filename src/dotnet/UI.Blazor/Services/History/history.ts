import { PromiseSource } from 'promises';
import { Log } from 'logging';

const { infoLog } = Log.get('History');

export class History {
    private static backendRef: DotNet.DotNetObject = null;
    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static init(backendRef1: DotNet.DotNetObject): void {
        this.backendRef = backendRef1;
        this.whenReady.resolve(undefined);
        globalThis["App"]["history"] = this;
    }

    public static async navigateTo(uri: string, mustReplace: boolean = false, force: boolean = false): Promise<void> {
        infoLog?.log(`navigateTo:`, uri, mustReplace, force);
        await this.whenReady;
        await this.backendRef.invokeMethodAsync('NavigateTo', uri, mustReplace, force);
    };
}
