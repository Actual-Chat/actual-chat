// TODO: fix eslint errors
/* eslint-disable @typescript-eslint/no-unsafe-assignment,@typescript-eslint/no-redundant-type-constituents,@typescript-eslint/no-explicit-any,@typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access,@typescript-eslint/no-unsafe-argument,@typescript-eslint/no-unnecessary-condition,@typescript-eslint/no-unsafe-return,@typescript-eslint/no-misused-promises,@typescript-eslint/no-unnecessary-type-conversion */
import { initializeApp } from 'firebase/app';
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw';
import { registerRoute, Route } from 'workbox-routing';
import { CacheFirst } from 'workbox-strategies';
import { ExpirationPlugin } from 'workbox-expiration';
import { CacheableResponsePlugin } from 'workbox-cacheable-response';
import { getLogs } from 'logging';
import { stopEvent } from 'event-handling';

const { debugLog, infoLog } = getLogs('ServiceWorker');
// @ts-expect-error intentional
self.__WB_DISABLE_DEV_LOGS = true; // disable workbox dev logs

// @ts-expect-error intentional
const sw = self as ServiceWorkerGlobalScope & typeof globalThis;
const configBase64 = new URL(location.href).searchParams.get('config');
// @ts-expect-error TODO: fix errors
const configString = atob(configBase64);
const config = JSON.parse(configString);

interface ExtendableEvent extends Event {
    waitUntil(f: Promise<any>): void;
}

interface NotificationEvent extends ExtendableEvent {
    readonly action: string;
    readonly notification: Notification;
}

sw.addEventListener('install', () => {
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

sw.addEventListener('message', (event: ExtendableEvent & { data?: any }) => {
    if (event.data?.type !== 'RECONCILE_NOTIFICATIONS')
        return;
    event.waitUntil(reconcileNotifications(event.data.active ?? [], event.data.createTags ?? []));
});

// Reconciles shown notifications against the server's active set: closes ones no longer active,
// and re-shows newly-active ones (createTags) that aren't shown — but only while no tab is
// visible (a visible tab's in-app UI already shows them).
const reconcileNotifications = async function(active: any[], createTags: string[]): Promise<void> {
    const activeTags = new Set<string>(active.map(x => x.tag));
    // getNotifications isn't available in every environment (some webviews/desktop) — skip the
    // close-stale pass rather than throwing.
    const shown = typeof sw.registration.getNotifications === 'function'
        ? await sw.registration.getNotifications()
        : [];
    const shownTags = new Set<string>();
    for (const notification of shown) {
        if (!notification.tag)
            continue;
        if (!activeTags.has(notification.tag))
            notification.close();
        else
            shownTags.add(notification.tag);
    }

    if (!createTags || createTags.length === 0)
        return;

    const clients = await sw.clients.matchAll({ type: 'window' });
    if (clients.some(c => c.visibilityState === 'visible'))
        return;

    for (const tag of createTags) {
        if (shownTags.has(tag))
            continue;
        const item = active.find(x => x.tag === tag);
        if (!item)
            continue;
        await sw.registration.showNotification(item.title, {
            tag: item.tag,
            icon: item.iconUrl,
            body: item.text,
            data: { url: item.url },
        });
    }
}

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
    const data = payload.data;
    if (!data)
        return;
    const dismissedTags = data.dismissedTags as string | undefined;
    if (dismissedTags) {
        const tags = dismissedTags.split(',');
        // getNotifications isn't available in every environment (some webviews/desktop); still
        // return so a dismissal payload never falls through to showing a banner.
        if (typeof sw.registration.getNotifications === 'function') {
            for (const dismissedTag of tags) {
                const toDismiss = await sw.registration.getNotifications({ tag: dismissedTag });
                for (const notification of toDismiss)
                    notification.close();
            }
        }
        // A cancelled/declined/timed-out call: route it to any open tab so the in-app ring banner
        // clears at once over the same channel that delivered the ring, instead of waiting on the
        // reactive live-session self-heal. Mirrors Android's ClearForegroundCallRings.
        const callTagPrefix = 'call-'; // Must match Constants.Notification.CallTagPrefix (no AppConstants in a SW)
        const cancelledCallChatIds = tags
            .filter(tag => tag.startsWith(callTagPrefix))
            .map(tag => tag.substring(callTagPrefix.length))
            .filter(chatId => chatId.length > 0);
        if (cancelledCallChatIds.length > 0) {
            const windowClients = await sw.clients.matchAll({ type: 'window' });
            for (const client of windowClients)
                for (const chatId of cancelledCallChatIds)
                    client.postMessage({ type: 'INCOMING_CALL_CANCELLED', chatId });
        }
        return;
    }
    // Incoming call: ring any open tab (in-app banner via OnRing) and show an OS notification so a
    // backgrounded/closed app still rings. The banner's own Accept/Decline take over once focused.
    if (data.kind === 'IncomingCall' && data.chatId) {
        const windowClients = await sw.clients.matchAll({ type: 'window' });
        for (const client of windowClients)
            client.postMessage({ type: 'INCOMING_CALL', chatId: data.chatId });

        await sw.registration.showNotification(payload.notification?.title ?? 'Incoming call', {
            tag: data.tag,
            icon: data.icon,
            body: payload.notification?.body ?? 'Incoming call',
            data: { url: data.link },
        });
        return;
    }
    // Skip the banner if the user is actively viewing this chat in a visible tab — they're
    // already seeing the messages; no OS notification needed (the server still dismisses it
    // for other devices once the read position advances).
    const link = data.link;
    if (link) {
        const chatUrl = new URL(link, self.location.origin);
        const chatHref = chatUrl.origin + chatUrl.pathname;
        const windowClients = await sw.clients.matchAll({ type: 'window' });
        if (windowClients.some(c => c.visibilityState === 'visible' && c.url.startsWith(chatHref)))
            return;
    }
    const tag = data.tag;
    const silent = String(data.silent ?? '').toLowerCase() === 'true';
    const options: NotificationOptions = {
        tag: tag.toString(),
        icon: data.icon,
        silent: silent,
        // @ts-expect-error renotify is valid per the Notifications spec but missing from lib DOM types
        renotify: !silent,
        // @ts-expect-error TODO: fix errors
        body: payload.notification.body,
        data: {
            // @ts-expect-error TODO: fix errors
            url: payload.fcmOptions.link,
        },
    };
    // silly hack because notifications get lost or suppressed
    if (typeof sw.registration.getNotifications === 'function') {
        const notificationsToClose = await sw.registration.getNotifications({ tag: tag });
        for (const toClose of notificationsToClose) {
            toClose.close();
        }
    }
    // @ts-expect-error TODO: fix errors
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
