import { getMessaging, getToken, GetTokenOptions, onMessage } from 'firebase/messaging';
import { Log } from 'logging';
import { AppKind } from '../UI.Blazor/Services/BrowserInfo/browser-info';
import { BrowserInit } from '../UI.Blazor/Services/BrowserInit/browser-init';

const { debugLog, warnLog, errorLog } = Log.get('NotificationUI');

export class NotificationUI {
    private static backendRef?: DotNet.DotNetObject = null;
    private static appKind?: AppKind = null;

    public static async init(backendRef: DotNet.DotNetObject, appKind: AppKind): Promise<void> {
        // probably init can be called multiple times on MAUI
        debugLog?.log(`init`);
        this.backendRef = backendRef;
        this.appKind = appKind;

        if (appKind === 'MauiApp')
            return;

        const state = await this.getPermissionState();
        await this.setPermissionState(state);
        this.registerNotificationHandler();
    }

    public static async getPermissionState(): Promise<PermissionState> {
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
        if (!status.onchange)
            status.onchange = _ => this.setPermissionState(status.state);
        return status.state;
    }

    public static async getDeviceToken(): Promise<string | null>
    {
        let { firebaseApp, firebasePublicKey} = BrowserInit;
        try {
            if (!firebaseApp) {
                firebaseApp = await BrowserInit.initFirebase();
                firebasePublicKey = BrowserInit.firebasePublicKey;
            }

            if (firebaseApp) {
                const messaging = getMessaging(firebaseApp);
                onMessage(messaging, (payload) => {
                    debugLog?.log(`onMessage, payload:`, payload);
                });

                const workerRegistration = await navigator.serviceWorker.getRegistration('sw.js');
                const tokenOptions: GetTokenOptions = {
                    vapidKey: firebasePublicKey,
                    serviceWorkerRegistration: workerRegistration,
                };
                return await getToken(messaging, tokenOptions);
            } else {
                warnLog?.log(`getDeviceToken: unable to initialize messaging subscription`);
            }
            return null;
        }
        catch (error) {
            errorLog?.log(`getDeviceToken: failed to obtain device token for notifications, error:`, error);
        }
    }

    public static async registerRequestNotificationHandler(element: HTMLElement): Promise<void> {
        element.addEventListener('click', this.requestNotificationPermissionHandler);
    }

    public static async unregisterRequestNotificationHandler(element: HTMLElement): Promise<void> {
        element.removeEventListener('click', this.requestNotificationPermissionHandler);
    }

    public static async requestNotificationPermission(): Promise<boolean> {
        debugLog?.log('requestNotificationPermission()');

        // Let's check if the browser supports notifications
        if (!('Notification' in window)) {
            warnLog?.log(`requestNotificationPermission: this browser doesn't support notifications`);
        } else {
            if (hasPromiseBasedNotificationApi()) {
                const permission = await Notification.requestPermission();
                storeNotificationsPermission(permission);
            } else {
                // Legacy browsers / Safari
                await new Promise<boolean>((resolve, reject) => {
                    try {
                        Notification.requestPermission(function(permission) {
                            storeNotificationsPermission(permission);
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

    // Private methods

    private static async setPermissionState(state: PermissionState): Promise<void> {
        debugLog?.log(`setPermissionState(${state})`);
        await this.backendRef.invokeMethodAsync('SetPermissionState', state);
    }

    private static registerNotificationHandler(): void {
        navigator.serviceWorker.addEventListener('message', async (event: MessageEvent) => {
            debugLog?.log(`navigator.serviceWorker.message:`, event);
            if (event.origin !== window.location.origin)
                return;
            if (event.type !== 'message' && event.data?.type !== 'NOTIFICATION_CLICK')
                return;

            const url = event.data?.url;
            await this.backendRef.invokeMethodAsync('HandleNotificationNavigation', url);
        });
    }

    // Must be lambda, otherwise "this" is going to be wrong here
    private static requestNotificationPermissionHandler = () => {
        void this.requestNotificationPermission();
    }
}

// Helpers

function storeNotificationsPermission(permission: NotificationPermission) {
    // Whatever the user answers, we make sure Chrome stores the information
    if (!('permission' in Notification)) {
        debugLog?.log(`storeNotificationsPermission(${permission})`);
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
//     const isGranted = await requestNotificationsPermission();
//     if (!isGranted)
//         errorLog?.log(`Notifications permission isn't granted`);
// });
