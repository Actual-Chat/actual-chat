import { initializeApp } from 'firebase/app';
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw';
import { registerRoute, Route } from 'workbox-routing';
import { CacheFirst } from 'workbox-strategies';
import { ExpirationPlugin } from 'workbox-expiration';
import { CacheableResponsePlugin } from 'workbox-cacheable-response';
import { Log } from 'logging';
import { stopEvent } from 'event-handling';

const { debugLog, infoLog } = Log.get('ServiceWorker');

// @ts-ignore
const sw = self as ServiceWorkerGlobalScope & typeof globalThis;
const configBase64 = new URL(location.href).searchParams.get('config');
const configString = atob(configBase64);
const config = JSON.parse(configString);

interface ExtendableEvent extends Event {
    waitUntil(f: Promise<any>): void;
}

interface NotificationEvent extends ExtendableEvent {
    readonly action: string;
    readonly notification: Notification;
}

sw.addEventListener('install', (event: ExtendableEvent) => {
    infoLog?.log(`install: installing updated service worker`);
    void sw.skipWaiting();
});

sw.addEventListener('activate', (event: ExtendableEvent) => {
    infoLog?.log(`activate: activating updated service worker`);
    event.waitUntil(sw.clients.claim());
});

sw.addEventListener('notificationclick', (event: NotificationEvent) => {
    event.waitUntil(onNotificationClick(event));
}, true);

const onNotificationClick = async function(event: NotificationEvent): Promise<any> {
    stopEvent(event);
    event.notification.close();

    const notificationUrl = event.notification?.data?.url;
    if (!notificationUrl)
        return;

    const url = new URL(notificationUrl);
    const href = url.href;
    const origin = url.origin;
    const hrefNotHashed = url.hash ? href.replace(url.hash, '') : href;

    const windowsClients = await sw.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const samePathWindow = windowsClients.find(wc => wc.url.startsWith(hrefNotHashed));
    const sameOriginWindow = windowsClients.find(wc => wc.url.startsWith(origin));

    const existingClientWindow = samePathWindow ?? sameOriginWindow;
    if (existingClientWindow) {
        const focusedWindow = await existingClientWindow.focus();
        focusedWindow.postMessage({
            type: 'NOTIFICATION_CLICK',
            url: url.href,
        });
        return;
    }

    const newClientWindow = await sw.clients.openWindow(url);
    if (newClientWindow)
        await newClientWindow.focus();
}

const app = initializeApp(config);
const messaging = getMessaging(app);
debugLog?.log(`Subscribing to FCM background messages`);
onBackgroundMessage(messaging, async payload => {
    debugLog?.log(`onBackgroundMessage: got FCM background message, payload:`, payload);
    const tag = payload.data.tag;
    const options: NotificationOptions = {
        tag: tag.toString(),
        icon: payload.data.icon,
        body: payload.notification.body,
        data: {
            url: payload.fcmOptions.link,
        },
    };
    // silly hack because notifications get lost or suppressed
    const notificationsToClose = await sw.registration.getNotifications({tag: tag});
    for (let toClose of notificationsToClose) {
        toClose.close();
    }
    await sw.registration.showNotification(payload.notification.title, options);
});

const imagesCacheStrategy = new CacheFirst({
    cacheName: 'images-cache',
    plugins: [
        new ExpirationPlugin({
            maxAgeSeconds: 30 * 24 * 60 * 60, // 30 days
        }),
        new CacheableResponsePlugin({
            statuses: [200],
        }),
    ],
});
const imagesRoute = new Route(
    ({ request }) => request.destination === 'image',
    imagesCacheStrategy,
);
registerRoute(imagesRoute);
