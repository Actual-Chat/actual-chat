import { Log } from 'logging';

const { warnLog } = Log.get('ServiceWorker');

export class ServiceWorker {
    public static async init(): Promise<void> {
        if ('serviceWorker' in navigator) {
            const origin = new URL('service-worker.ts', import.meta.url).origin;
            const workerPath = new URL('/sw.js', origin).toString();
            const workerRegistration = await navigator.serviceWorker.register(
                workerPath,
                { scope: '/', updateViaCache: 'all' });
            workerRegistration.addEventListener('updatefound', () => {
                warnLog?.log(`updateFound: updated service worker detected`);
            });
        }
    }
}
