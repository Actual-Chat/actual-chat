import { PromiseSource } from 'promises';
import { Log } from 'logging';

const { infoLog } = Log.get('History');

export class History {
    private static backendRef: DotNet.DotNetObject = null;

    public static navigationManager: any
    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static init(
        backendRef1: DotNet.DotNetObject,
        url: string,
        historyEntryState: string
    ): void {
        this.backendRef = backendRef1;
        this.navigationManager = window['Blazor']._internal.navigationManager;

        const options = {
            forceLoad : false,
            replaceHistoryEntry : true,
            historyEntryState : historyEntryState
        };
        this.navigationManager.navigateTo(url, options);
        this.whenReady.resolve(undefined);
        globalThis["App"]["history"] = this;
    }

    public static async navigateTo(
        uri: string,
        mustReplace: boolean = false,
        force: boolean = false,
        addInFront: boolean = false
    ): Promise<void> {
        infoLog?.log(`navigateTo:`, uri, mustReplace, force, addInFront);
        await this.whenReady;
        await this.backendRef.invokeMethodAsync('NavigateTo', uri, mustReplace, force, addInFront);
    };
}
