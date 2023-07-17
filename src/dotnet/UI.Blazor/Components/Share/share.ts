import {Log} from "logging";


const { infoLog } = Log.get('Share');

export class Share {
    private static backendRef: DotNet.DotNetObject = null;

    public static initWebShareInfo(backendRef1: DotNet.DotNetObject): void {
        infoLog?.log(`initializing`);
        this.backendRef = backendRef1;

        // Call OnInitialized
        const initResult: InitResult = {
            canShareText: Share.canShareSafe({
                text: 'Actual Chat',
            }),
            canShareLink: Share.canShareSafe({
                url: 'https://actual.chat/',
            }),
        };
        infoLog?.log(`init:`, JSON.stringify(initResult));
        void this.backendRef.invokeMethodAsync('OnInitialized', initResult);
    }

    public static canShare() : boolean {
        return navigator.canShare();
    }

    public static shareLink(title : string, link: string) : Promise<void> {
        return navigator.share({
            title : title,
            url : link
        });
    }

    public static shareText(title : string, text: string) : Promise<void> {
        return navigator.share({
            title : title,
            text : text
        });
    }

    public static onClick = (event : Event) => {
        const target = event.currentTarget as HTMLElement;
        if (!target)
            return;
        const link = target.dataset.shareLink;
        const text = target.dataset.shareText;
        const title = target.dataset.shareTitle;
        if (link) {
            void navigator.share({
                title : title ?? 'Share link',
                url : link,
            });
        }
        else if (text) {
            void navigator.share({
                title : title,
                text : text
            });
        }
    }

    private static canShareSafe(data?: ShareData): boolean {
        return navigator.canShare && navigator.canShare(data);
    }
}

interface InitResult {
    canShareText: boolean,
    canShareLink: boolean,
}
