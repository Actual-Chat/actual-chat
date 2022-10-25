import { initializeApp } from 'firebase/app';
import { getMessaging, getToken, GetTokenOptions, onMessage } from 'firebase/messaging';
import { NextInteraction } from 'next-interaction';
import { Log, LogLevel } from '../../nodejs/src/logging';

const LogScope = 'MessagingInit';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const infoLog = Log.get(LogScope, LogLevel.Info);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export async function getDeviceToken(): Promise<string | null> {
    try {
        const response = await fetch('/dist/config/firebase.config.js');
        if (response.ok || response.status === 304) {
            const { config, publicKey } = await response.json();
            const configBase64 = btoa(JSON.stringify(config));
            const app = initializeApp(config);
            const messaging = getMessaging(app);
            onMessage(messaging, (payload) => {
                debugLog?.log(`onMessage: payload:`, payload);
            });

            const origin = new URL('messaging-init.ts', import.meta.url).origin;
            const workerPath = new URL('/sw.js', origin).toString();
            const workerUrl = `${workerPath}?config=${configBase64}`;

            const workerRegistration = await navigator.serviceWorker.register(workerUrl, { scope: '/', updateViaCache: 'all' });
            workerRegistration.addEventListener('updatefound', ev => {
                warnLog?.log(`updateFound: updated service worker detected`);
            });

            const tokenOptions: GetTokenOptions = {
                vapidKey: publicKey,
                serviceWorkerRegistration: workerRegistration,
            };
            return await getToken(messaging, tokenOptions);
        } else {
            warnLog?.log(`getDeviceToken: unable to initialize messaging subscription, status: ${response.status}`)
        }
        return null;
    }
    catch (error) {
        errorLog?.log(`getDeviceToken: failed to obtain device token for notifications, error:`, error);
    }
}

export async function requestNotificationPermission(): Promise<boolean> {
    // Let's check if the browser supports notifications
    if (!('Notification' in window)) {
        warnLog?.log(`requestNotificationPermission: this browser doesn't support notifications`);
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
            debugLog?.log(`navigator.serviceWorker.message:`, evt);
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
        debugLog?.log(`storeNotificationPermission, permission:`, permission);
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
        errorLog?.log(`Notification permission isn't granted`);
});
