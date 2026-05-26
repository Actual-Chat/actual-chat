/* eslint-disable */
import { PromiseSource } from 'actuallab-core';
import { getLogs } from 'logging';
import { BrowserInfo } from '../BrowserInfo/browser-info';

const { infoLog } = getLogs('History');

export class History {
    private static backendRef: DotNet.DotNetObject = null!;

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

let testAnchor: HTMLAnchorElement;
export function toAbsoluteUri(relativeUri: string): string {
    testAnchor = testAnchor || document.createElement('a');
    testAnchor.href = relativeUri;
    return testAnchor.href;
}

export function isSamePageWithHash(absoluteHref: string): boolean {
    const url = new URL(absoluteHref);
    return url.hash !== '' && location.origin === url.origin && location.pathname === url.pathname && location.search === url.search;
}
