import { initializeApp } from 'firebase/app';
import { getMessaging, getToken, GetTokenOptions, onMessage } from 'firebase/messaging';
import { Log } from 'logging';
import { AppKind } from '../UI.Blazor/Services/BrowserInfo/browser-info';

const { debugLog, warnLog, errorLog } = Log.get('NotificationUI');

export class NotificationUI {
    private static backendRef?: DotNet.DotNetObject = null;
    private static appKind?: AppKind = null;

    public static async init(backendRef: DotNet.DotNetObject, appKind: AppKind): Promise<void> {
        // probably init can be called multiple times on MAUI
        debugLog?.log('init');
        NotificationUI.backendRef = backendRef;
        NotificationUI.appKind = appKind;

        if (appKind === 'MauiApp')
            return;

        const status = await NotificationUI.getNotificationPermissionStatus();
        await NotificationUI.updateNotificationStatus(status);
        NotificationUI.registerNotificationHandler();
    }

    public static async registerRequestNotificationHandler(buttonContainer: HTMLElement): Promise<void> {
        buttonContainer.addEventListener('click', async () => {
            await NotificationUI.requestNotificationPermission();
            await NotificationUI.getDeviceToken();
        });
    }

    public static async getNotificationPermissionStatus(): Promise<PermissionState> {
        if (!('Notification' in window))
            return 'denied';

        if (!('permission' in Notification))
            return 'denied';

        if (Notification.permission === 'granted')
            return 'granted';

        if (!('permissions' in navigator))
            return Notification.permission === 'denied'
                ? 'denied'
                : 'prompt';

        if (!('query' in navigator.permissions))
            return Notification.permission === 'denied'
               ? 'denied'
               : 'prompt';

        const status = await navigator.permissions.query({ name: 'notifications' });
        if (!status.onchange) {
            status.onchange = ev => NotificationUI.updateNotificationStatus(status.state);
        }
        return status.state;
    }

    public static async getDeviceToken(): Promise<string | null>
    {
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

                const origin = new URL('notification-ui.ts', import.meta.url).origin;
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
                warnLog?.log(`getDeviceToken: unable to initialize messaging subscription, status: ${response.status}`);
            }
            return null;
        }
        catch (error) {
            errorLog?.log(`getDeviceToken: failed to obtain device token for notifications, error:`, error);
        }
    }

    public static async requestNotificationPermission(): Promise<boolean> {
        debugLog?.log('requestNotificationPermission()');

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

    private static async updateNotificationStatus(state: PermissionState): Promise<void> {
        debugLog?.log('-> updateNotificationStatus');
        await NotificationUI.backendRef.invokeMethodAsync('UpdateNotificationStatus', state);
        debugLog?.log('<- updateNotificationStatus');
    }

    private static registerNotificationHandler(): void {
        navigator.serviceWorker.addEventListener('message', async (evt: MessageEvent) => {
            debugLog?.log(`navigator.serviceWorker.message:`, evt);
            if (evt.origin !== window.location.origin)
                return;
            if (evt.type !== 'message' && evt.data?.type !== 'NOTIFICATION_CLICK')
                return;

            const url = evt.data?.url;
            await NotificationUI.backendRef.invokeMethodAsync('HandleNotificationNavigation', url);
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

// Interactive.whenInteractive().then(async () => {
//     if (BrowserInfo.appKind == 'Maui')
//         return;
//
//     const isGranted = await requestNotificationPermission();
//     if (!isGranted)
//         errorLog?.log(`Notification permission isn't granted`);
// });
