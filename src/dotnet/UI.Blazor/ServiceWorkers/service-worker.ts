import { initializeApp } from 'firebase/app';
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw';

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

sw.addEventListener('notificationclick', (event: NotificationEvent) => {
    event.waitUntil(onNotificationClick(event));
}, true);

const app = initializeApp(config);
const messaging = getMessaging(app);
onBackgroundMessage(messaging, async payload => {
    console.log('[messaging-service-worker.ts] Received background message ', payload);
    const chatId = payload.notification.chatId;
    const notificationsToClose = await sw.registration.getNotifications({tag: chatId});
    for (let toClose of notificationsToClose) {
        toClose.close();
    }
    const notification = Object.assign({tag: chatId}, payload.notification);
    await sw.registration.showNotification(notification.title, notification);
});


// @ts-ignore
const onNotificationClick = async function(event: NotificationEvent): Promise<any> {
    event.stopImmediatePropagation();
    event.notification.close();

    const notificationUrl = event.notification?.data?.FCM_MSG?.notification?.click_action;
    if (!notificationUrl)
        return;

    const url = new URL(notificationUrl);
    const href = url.href;
    const origin = url.origin;
    const hrefNotHashed = url.hash ? href.replace(url.hash, '') : href;
    // @ts-ignore
    const clients: Clients = sw.clients;

    const windowsClients = await clients.matchAll({ type: 'window', includeUncontrolled: true });
    const samePathWindow = windowsClients.find(wc => wc.url.startsWith(hrefNotHashed));
    const sameOriginWindow = windowsClients.find(wc => wc.url.startsWith(origin));

    const existingClientWindow = samePathWindow ?? sameOriginWindow;
    if (existingClientWindow) {
        await clients.claim();
        const focusedWindow = await existingClientWindow.focus();
        focusedWindow.postMessage({
            type: 'NOTIFICATION_CLICK',
            url: url.href,
        });
        return;
    }

    const newClientWindow = await clients.openWindow(url);
    if (newClientWindow)
        await newClientWindow.focus();
}
