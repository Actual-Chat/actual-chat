import { initializeApp } from 'firebase/app';
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw';

const LogScope = 'MessagingServiceWorker';
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
    console.info(`${LogScope}: making fresh installed service worker active`);
    void sw.skipWaiting();
});

sw.addEventListener('activate', (event: ExtendableEvent) => {
    console.info(`${LogScope}: forcing fresh activated service worker to start controlling pages`);
    event.waitUntil(sw.clients.claim());
});

sw.addEventListener('notificationclick', (event: NotificationEvent) => {
    event.waitUntil(onNotificationClick(event));
}, true);

const onNotificationClick = async function(event: NotificationEvent): Promise<any> {
    event.stopImmediatePropagation();
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
console.info(`${LogScope}: Subscribing on fcm background message`);
onBackgroundMessage(messaging, async payload => {
    console.info(`${LogScope}: Received background message `, payload);
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
