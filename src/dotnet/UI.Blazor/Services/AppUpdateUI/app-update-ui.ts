import { getLogs } from 'logging';

const { warnLog } = getLogs('AppUpdateUI');

interface RelatedApplication {
    platform: string;
    id?: string;
    url?: string;
    version?: string;
}

// getInstalledRelatedApps() is Chromium-only, so it's absent from the DOM typings we use.
type NavigatorWithRelatedApps = Navigator & {
    getInstalledRelatedApps?: () => Promise<RelatedApplication[]>;
};

export class AppUpdateUI {
    /** Null means the probe threw; an empty array means the browser answered "none installed". */
    public static async getInstalledAppIds(): Promise<string[] | null> {
        const nav = navigator as NavigatorWithRelatedApps;
        if (typeof nav.getInstalledRelatedApps !== 'function')
            return [];

        try {
            const apps = await nav.getInstalledRelatedApps();
            return apps.map(x => x.id).filter((x): x is string => !!x);
        }
        catch (e) {
            warnLog?.log('getInstalledAppIds: failed', e);
            return null;
        }
    }

    /** Assigns rather than window.open: an intent:// URL opened in a new tab leaves that tab blank. */
    public static openApp(url: string): void {
        window.location.href = url;
    }
}
