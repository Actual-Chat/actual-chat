import { initializeApp } from 'firebase/app';
import { getMessaging, getToken, GetTokenOptions, onMessage } from 'firebase/messaging';
import { NextInteraction } from 'next-interaction';

const LogScope = 'MessagingInit';

export async function getDeviceToken(): Promise<string | null> {
    try {
        const response = await fetch('/dist/config/firebase.config.js');
        if (response.ok || response.status === 304) {
            const { config, publicKey } = await response.json();
            const configBase64 = btoa(JSON.stringify(config));
            const app = initializeApp(config);
            const messaging = getMessaging(app);
            onMessage(messaging, (payload) => {
                console.info(`${LogScope}: Message received. `, payload);
            });

            const origin = new URL('messaging-init.ts', import.meta.url).origin;
            const workerPath = new URL('/sw.js', origin).toString();
            const workerUrl = `${workerPath}?config=${configBase64}`;

            const workerRegistration = await navigator.serviceWorker.register(workerUrl, { scope: '/', updateViaCache: 'all' });
            workerRegistration.addEventListener('updatefound', ev => {
                console.info(`${LogScope}: fresher service-worker detected`);
            });

            const tokenOptions: GetTokenOptions = {
                vapidKey: publicKey,
                serviceWorkerRegistration: workerRegistration,
            };
            return await getToken(messaging, tokenOptions);
        } else {
            console.warn(`${LogScope}: Unable to initialize messaging subscription. Status: ${response.status}`)
        }
        return null;
    }
    catch(e) {
        console.error(`${LogScope}: failed to obtain device token for notifications`, e);
    }
}

export async function requestNotificationPermission(): Promise<boolean> {
    // Let's check if the browser supports notifications
    if (!('Notification' in window)) {
        console.info(`${LogScope}: This browser does not support notifications.`);
    } else {
        if (hasPromiseBasedNotificationApi()) {
            const permission = await Notification.requestPermission();
            storeNotificationPermission(permission);
        } else {
            // Legacy browsers / safari
            await new Promise<boolean>((resolve, reject) => {
                try {
                    Notification.requestPermission(function(permission) {
                        storeNotificationPermission(permission);
                        resolve(true);
                    });
                }
                catch (e) {
                    reject(e);
                }
            });
        }
        return Notification.permission === 'granted';
    }
}

let baseLayoutRef: DotNet.DotNetObject = null;

export function registerNotificationHandler(blazorRef: DotNet.DotNetObject): void {
    const isAlreadyRegistered = baseLayoutRef !== null;
    baseLayoutRef = blazorRef;
    if (!isAlreadyRegistered) {
        navigator.serviceWorker.addEventListener('message', async (evt: MessageEvent) => {
            console.info(`${LogScope}: message event from service worker`, evt);
            if (evt.origin !== window.location.origin)
                return;
            if (evt.type !== 'message' && evt.data?.type !== 'NOTIFICATION_CLICK')
                return;

            const url = evt.data?.url;
            await baseLayoutRef.invokeMethodAsync('HandleNotificationNavigation', url);
        });
    }
}


function storeNotificationPermission(permission) {
    // Whatever the user answers, we make sure Chrome stores the information
    if (!('permission' in Notification)) {
        console.info(`${LogScope}: storing notification permission`, permission);
        // @ts-ignore readonly property
        Notification['permission'] = permission;
    }
}

function hasPromiseBasedNotificationApi(): boolean {
    try {
        Notification.requestPermission().then();
        return true;
    } catch(e) {
        return false;
    }
}

NextInteraction.addHandler(async () => {
    const isGranted = await requestNotificationPermission();
    if (!isGranted)
        console.error(`${LogScope}: Notifications are disabled.`);
});
