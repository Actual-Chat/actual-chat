import { initializeApp } from 'firebase/app';
import { getMessaging, getToken, onMessage, GetTokenOptions } from 'firebase/messaging';

export async function initializeMessaging(): Promise<void> {
    try {
        const response = await fetch('/dist/config/firebase.config.json');
        if (response.ok || response.status === 304) {
            const { config, publicKey } = await response.json();
            const app = initializeApp(config);
            const messaging = getMessaging(app);
            const origin = new URL('messaging-init.ts', import.meta.url).origin;
            const swPath = new URL('/dist/messagingServiceWorker.js', origin).toString();
            const swRegistration = await navigator.serviceWorker.register(swPath, {
                scope: '/dist/firebase-cloud-messaging-push-scope'
            });
            const tokenOptions: GetTokenOptions = {
                vapidKey: publicKey,
                serviceWorkerRegistration: swRegistration,
            };
            const token = await getToken(messaging, tokenOptions);
            // TODO(AK): save token to the server
            console.log(token);

            onMessage(messaging, (payload) => {
                console.log('Message received. ', payload);
            });
        } else {
            console.warn(`Unable to initialize messaging subscription. Status: ${response.status}`)
        }
    }
    catch(e) {
        console.error(e);
    }
}

export function askNotificationPermission(): boolean {
    function handlePermission(permission) {
        // Whatever the user answers, we make sure Chrome stores the information
        if(!('permission' in Notification)) {
            Notification.permission = permission;
        }
    }

    // Let's check if the browser supports notifications
    if (!"Notification" in window) {
        console.log("This browser does not support notifications.");
    } else {
        if(checkNotificationPromise()) {
            Notification.requestPermission()
                .then((permission) => {
                    handlePermission(permission);
                })
        } else {
            Notification.requestPermission(function(permission) {
                handlePermission(permission);
            });
        }
    }
}

// Function to check whether browser supports the promise version of requestPermission()
// Safari only supports the old callback-based version
function checkNotificationPromise(): boolean {
    try {
        Notification.requestPermission().then();
    } catch(e) {
        return false;
    }

    return true;
}


void initializeMessaging();
