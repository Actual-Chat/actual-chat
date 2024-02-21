﻿import { Log } from "logging";

const { infoLog } = Log.get('Share');

export class Share {
    private static backendRef: DotNet.DotNetObject = null;

    /** Called from Blazor  */
    public static init(backendRef1: DotNet.DotNetObject): void {
        this.backendRef = backendRef1;

        const initResult: InitResult = {
            canShareText: this.canShare({
                text: 'Actual Chat',
            }),
            canShareLink: this.canShare({
                url: 'https://actual.chat/',
            }),
        };
        infoLog?.log(`init:`, initResult);
        void this.backendRef.invokeMethodAsync('OnInitialized', initResult);
    }

    /** Called from Blazor  */
    public static canShare(data?: ShareData): boolean {
        return navigator.canShare && navigator.canShare(data);
    }

    /** Called from Blazor  */
    public static registerHandler(): void {
        const buttons = [...document.querySelectorAll<HTMLButtonElement>('div.share-externally-button > button')];
        buttons.forEach(btn => btn.addEventListener('click', Share.onClick));
    }

    // Private methods

    private static async shareLink(title: string, link: string) : Promise<boolean> {
        const data = {
            title: title,
            url: link
        };
        if (!this.canShare(data))
            return false;

        await navigator.share(data);
        return true;
    }

    private static async shareText(title: string, text: string) : Promise<boolean> {
        const data = {
            title: title,
            text: text
        };
        if (!this.canShare(data))
            return false;

        await navigator.share(data);
        return true;
    }

    private static onClick = async (event: Event): Promise<void> => {
        const target = event.currentTarget as HTMLElement;
        if (!target)
            return;

        const title = target.dataset.shareTitle;
        const link = target.dataset.shareLink;
        if (link && await this.shareLink(title, link)) // Link share is preferred over text share
            return;

        const text = target.dataset.shareText;
        if (text)
            await this.shareText(title, text);
    }
}

interface InitResult {
    canShareText: boolean,
    canShareLink: boolean,
}
