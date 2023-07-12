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
        url: string,
        mustReplace = false,
        force = false,
        addInFront = false
    ): Promise<void> {
        infoLog?.log(`navigateTo:`, url, mustReplace, force, addInFront);
        await this.whenReady;
        await this.backendRef.invokeMethodAsync('NavigateTo', url, mustReplace, force, addInFront);
    };

    public static async forceReload(url: string, mustReplace: boolean, historyEntryState: string) {
        await this.whenReady;
        const options = {
            forceLoad : true,
            replaceHistoryEntry : true,
            historyEntryState : historyEntryState
        };
        this.navigationManager.navigateTo(url, options);
    }
}
