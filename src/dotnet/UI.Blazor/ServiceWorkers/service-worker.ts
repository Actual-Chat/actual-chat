import { initializeApp } from 'firebase/app';
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw';
import { Log, LogLevel, LogScope } from 'logging';
import { endEvent } from 'event-handling';

const LogScope: LogScope = 'ServiceWorker';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const infoLog = Log.get(LogScope, LogLevel.Info);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

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
    endEvent(event);
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
