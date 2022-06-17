import { initializeApp } from 'firebase/app';
import { getMessaging, getToken, GetTokenOptions, onMessage,  } from 'firebase/messaging';
import { addInteractionHandler } from '../../nodejs/src/first-interaction';

const LogScope = 'MessagingInit';

enum MessageType {
    NOTIFICATION_CLICKED = "notification-clicked"
}

export function registerNotificationClickHandler(): void {
    window.addEventListener("message", ev => {
        console.log('message !!!!')
        if (ev.origin !== window.origin)
            return;

        const { type, link } = ev;
        if (link && type == MessageType.NOTIFICATION_CLICKED) {
            console.log(link);
            window.location.href = link;
        }
    });

    console.log('registered message handler!!!');
}

export async function getDeviceToken(): Promise<string | null> {
    try {
        const response = await fetch('/dist/config/firebase.config.js');
        if (response.ok || response.status === 304) {
            const { config, publicKey } = await response.json();
            const configBase64 = btoa(JSON.stringify(config));
            const app = initializeApp(config);
            const messaging = getMessaging(app);
            onMessage(messaging, (payload) => {
                console.log('Message received. ', payload);
            });

            const origin = new URL('messaging-init.ts', import.meta.url).origin;
            const workerPath = new URL('/dist/messagingServiceWorker.js', origin).toString();
            const workerUrl = `${workerPath}?config=${configBase64}`;
            let workerRegistration = await navigator.serviceWorker.getRegistration(workerUrl);
            if (!workerRegistration) {
                workerRegistration = await navigator.serviceWorker.register(workerUrl, {
                    scope: '/dist/firebase-cloud-messaging-push-scope'
                });
            }

            const tokenOptions: GetTokenOptions = {
                vapidKey: publicKey,
                serviceWorkerRegistration: workerRegistration,
            };
            return await getToken(messaging, tokenOptions);
        } else {
            console.warn(`Unable to initialize messaging subscription. Status: ${response.status}`)
        }
        return null;
    }
    catch(e) {
        console.error(e);
    }
    finally {
        registerNotificationClickHandler();
    }
}

export async function requestNotificationPermission(): Promise<boolean> {
    // Let's check if the browser supports notifications
    if (!('Notification' in window)) {
        console.log('This browser does not support notifications.');
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

function storeNotificationPermission(permission) {
    // Whatever the user answers, we make sure Chrome stores the information
    if (!('permission' in Notification)) {
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

addInteractionHandler(LogScope, async () => {
    const isGranted = await requestNotificationPermission();
    if (!isGranted)
        throw `${LogScope}: Notifications are disabled.`;

    // Notification permissions are granted on touchend
    // that follows scroll, but this isn't what considered
    // "user interaction" w/ AudioContext, so we always
    // return false here.
    // Only AudioContextLazy returns true when it
    // completes the initialization.
    return false;
});
