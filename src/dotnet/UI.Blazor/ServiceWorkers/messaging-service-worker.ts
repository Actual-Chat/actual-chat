/// <reference no-default-lib="true"/>
/// <reference lib="es2020" />
/// <reference lib="WebWorker" />

import { initializeApp } from 'firebase/app';
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw';

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
onBackgroundMessage(messaging, payload => {
    console.log('[messaging-service-worker.ts] Received background message ', payload);
    // payload.
});


const onNotificationClick = async function(event: NotificationEvent): Promise<any> {
    console.log('shit 5');

    event.stopImmediatePropagation();
    event.notification.close();
    // if (event.action === 'close')
    //     return;

    const notificationUrl = event.notification?.data?.FCM_MSG?.notification?.click_action;
    if (!notificationUrl)
        return;

    const url = new URL(notificationUrl);
    const href = url.href;
    const origin = url.origin;
    const hrefNotHashed = url.hash ? href.replace(url.hash, '') : href;
    const clients: Clients = sw.clients;

    const windowsClients = await clients.matchAll({ type: 'window', includeUncontrolled: true });
        // .claim()
    const samePathWindow = windowsClients.find(wc => wc.url.startsWith(hrefNotHashed));
    const sameOriginWindow = windowsClients.find(wc => wc.url.startsWith(origin));

    const existingClientWindow = samePathWindow ?? sameOriginWindow;
    if (existingClientWindow) {
        // const client = await existingClientWindow.navigate(url);
        // if (client)
        //     await client.focus();
        const focusedWindow = await existingClientWindow.focus();
        // await focusedWindow.navigate('https://local.actual.chat/dist/firebase-cloud-messaging-push-scope/1');
        focusedWindow.postMessage({ type: 'notification-click', link: href });
        // await existingClientWindow.navigate(url);
        return;
    }

    const newClientWindow = await clients.openWindow(url);
    if (newClientWindow)
        await newClientWindow.focus();
}
