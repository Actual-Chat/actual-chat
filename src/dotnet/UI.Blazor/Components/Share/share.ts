import { AC } from 'app-constants';
import { getLogs } from 'logging';

const { infoLog, warnLog } = getLogs('Share');

export class Share {
    private static readonly filePromises = new Map<string, Promise<File>>();

    /** Called from Blazor  */
    public static init(backendRef1: DotNet.DotNetObject): void {
        const initResult: InitResult = {
            canShareText: this.canShare({
                text: AC.appName,
            }),
            canShareLink: this.canShare({
                url: `https://${AC.prodHost}/`,
            }),
            canShareFile: this.canShareFile(),
        };
        infoLog?.log(`init:`, initResult);
        void backendRef1.invokeMethodAsync('OnInitialized', initResult);
    }

    /** Called from Blazor  */
    public static canShare(data?: ShareData): boolean {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        return !!navigator.canShare && navigator.canShare(data);
    }

    /** Called from Blazor  */
    public static registerHandler(): void {
        const buttons = [...document.querySelectorAll<HTMLButtonElement>('div.share-externally-button > button')];
        buttons.forEach(btn => {
            if (btn.dataset.shareHandlerRegistered)
                return;

            btn.dataset.shareHandlerRegistered = 'true';
            // The download starts as soon as the button appears, and again on pointerdown for
            // whatever it missed: Safari drops the share() call once the user activation the
            // click granted expires, which a download still running would outlast.
            btn.addEventListener('pointerdown', Share.onPointerDown);
            btn.addEventListener('click', (event) => { void Share.onClick(event); });
            Share.getFileRefs(btn)?.forEach(ref => { void Share.getFile(ref); });
        });
    }

    // Private methods

    private static canShareFile(): boolean {
        try {
            const probe = new File([new Blob([''], { type: 'image/png' })], 'probe.png', { type: 'image/png' });
            return this.canShare({ files: [probe] });
        }
        catch (e) {
            warnLog?.log('canShareFile: failed', e);
            return false;
        }
    }

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

    private static async shareFiles(target: HTMLElement, refs: SharedMedia[]) : Promise<void> {
        // Downloading the files takes a while, and a second click would start a second share
        const button = target instanceof HTMLButtonElement ? target : null;
        if (button)
            button.disabled = true;
        try {
            const files = await Promise.all(refs.map(ref => this.getFile(ref)));
            let data: ShareData = { files: files };
            if (target.dataset.shareTitle)
                data.title = target.dataset.shareTitle;
            if (target.dataset.shareText)
                data.text = target.dataset.shareText;
            if (!this.canShare(data)) // Some share targets reject files combined with a text
                data = { files: files };
            if (!this.canShare(data)) {
                warnLog?.log('shareFiles: files are not shareable');
                return;
            }

            await navigator.share(data);
        }
        catch (e) {
            // AbortError means the user dismissed the share sheet
            if (!(e instanceof DOMException) || e.name !== 'AbortError')
                warnLog?.log('shareFiles: failed', e);
        }
        finally {
            if (button)
                button.disabled = false;
            // The blobs are cached only to bridge pointerdown -> click
            this.filePromises.clear();
        }
    }

    private static getFileRefs(target: HTMLElement): SharedMedia[] | null {
        const holders = target.parentElement?.querySelectorAll<HTMLElement>(':scope > .c-share-file');
        if (!holders?.length)
            return null;

        return [...holders].map(holder => ({
            url: holder.dataset.url ?? '',
            fileName: holder.dataset.fileName ?? '',
            contentType: holder.dataset.contentType ?? '',
        }));
    }

    private static getFile(ref: SharedMedia): Promise<File> {
        let filePromise = this.filePromises.get(ref.url);
        if (!filePromise) {
            filePromise = this.fetchFile(ref);
            this.filePromises.set(ref.url, filePromise);
            filePromise.catch(() => this.filePromises.delete(ref.url));
        }
        return filePromise;
    }

    private static async fetchFile(ref: SharedMedia): Promise<File> {
        const response = await fetch(ref.url);
        if (!response.ok)
            throw new Error(`Share.fetchFile: HTTP ${response.status} for '${ref.url}'`);

        const blob = await response.blob();
        return new File([blob], ref.fileName, { type: ref.contentType || blob.type });
    }

    private static onPointerDown = (event: Event): void => {
        const target = event.currentTarget as HTMLElement;
        // TODO: fix eslint error
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!target)
            return;

        this.getFileRefs(target)?.forEach(ref => { void this.getFile(ref); });
    }

    private static onClick = async (event: Event): Promise<void> => {
        const target = event.currentTarget as HTMLElement;
        // TODO: fix eslint error
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!target)
            return;

        const fileRefs = this.getFileRefs(target);
        if (fileRefs) {
            await this.shareFiles(target, fileRefs);
            return;
        }

        const title = target.dataset.shareTitle;
        const link = target.dataset.shareLink;
        if (link && title && await this.shareLink(title, link)) // Link share is preferred over text share
            return;

        const text = target.dataset.shareText;
        if (text && title)
            await this.shareText(title, text);
    }
}

interface InitResult {
    canShareText: boolean,
    canShareLink: boolean,
    canShareFile: boolean,
}

interface SharedMedia {
    url: string,
    fileName: string,
    contentType: string,
}
