import { Log } from 'logging';
import { Versioning } from 'versioning';
import { BrowserInfo } from '../../dotnet/UI.Blazor/Services/BrowserInfo/browser-info';

const { warnLog } = Log.get('ServiceWorker');

export class ServiceWorker {
    public static async init(): Promise<void> {
        if (BrowserInfo.hostKind === 'MauiApp')
            return;

        if (!('serviceWorker' in navigator))
            return;

        const firebaseConfigUrl = Versioning.mapPath('/dist/config/firebase.config.js');
        const response = await fetch(firebaseConfigUrl);
        if (response.ok || response.status === 304) {
            // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
            const { config } = await response.json();
            // eslint-disable-next-line @typescript-eslint/no-deprecated
            const configBase64 = btoa(JSON.stringify(config));
            const origin = new URL('service-worker.ts', import.meta.url).origin;
            const workerPath = new URL('/sw.js', origin).toString();
            const workerUrl = `${workerPath}?config=${configBase64}`;
            const workerRegistration = await navigator.serviceWorker.register(
                workerUrl,
                { scope: '/', updateViaCache: 'all' });
            workerRegistration.addEventListener('updatefound', () => {
                warnLog?.log(`updateFound: updated service worker detected`);
            });
        } else {
            warnLog?.log(`init: unable to get firebase config, status: ${response.status}`);
        }
    }
}
