import { PromiseSource } from 'promises';
import { Log } from 'logging';

const { infoLog } = Log.get('History');

export class History {
    private static backendRef: DotNet.DotNetObject = null;
    public static whenReady: PromiseSource<void> = new PromiseSource<void>();

    public static init(backendRef1: DotNet.DotNetObject, historyEntryState : string): void {
        this.backendRef = backendRef1;
        // init history state for initial location by calling Blazor internal API from here to avoid additional interop
        const options = { forceLoad : false, replaceHistoryEntry : true, historyEntryState : historyEntryState };
        const navigationManager = window.window['Blazor']._internal.navigationManager;
        navigationManager.navigateTo(location.href, options);
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
