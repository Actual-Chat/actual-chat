import { initializeApp } from 'firebase/app';
import { getMessaging, getToken, GetTokenOptions, onMessage } from 'firebase/messaging';

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

// TODO(AK): Move to more natural place, like joining a chat, etc.
function addInitEventListeners() : void {
    self.addEventListener('click', onInitEvent);
    self.addEventListener('doubleclick', onInitEvent);
    self.addEventListener('onkeydown', onInitEvent);
    self.addEventListener('touchend', onInitEvent);
}

function removeInitEventListeners() : void {
    self.removeEventListener('click', onInitEvent);
    self.removeEventListener('doubleclick', onInitEvent);
    self.removeEventListener('onkeydown', onInitEvent);
    self.removeEventListener('touchend', onInitEvent);
}

const onInitEvent = async () => {
    removeInitEventListeners();
    const isGranted = await requestNotificationPermission();
    if (!isGranted)
        console.log(`${LogScope}: Notifications are disabled.`);
}

addInitEventListeners();
