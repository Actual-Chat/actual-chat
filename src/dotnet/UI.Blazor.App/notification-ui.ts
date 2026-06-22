// TODO: remove eslint-disable and fix errors
/* eslint-disable @typescript-eslint/no-floating-promises,@typescript-eslint/prefer-promise-reject-errors,@typescript-eslint/no-misused-promises,@typescript-eslint/require-await,@typescript-eslint/no-unsafe-member-access,@typescript-eslint/no-unsafe-assignment */
import { getMessaging, getToken, deleteToken, GetTokenOptions, onMessage } from 'firebase/messaging';
import { getLogs } from 'logging';
import { HostKind } from '../UI.Blazor/Services/BrowserInfo/browser-info';
import { BrowserInit } from '../UI.Blazor/Services/BrowserInit/browser-init';

const { debugLog, warnLog, errorLog } = getLogs('NotificationUI');

interface ActiveNotificationInfo {
    tag: string;
    title: string;
    text: string;
    iconUrl: string;
    url: string;
}

export class NotificationUI {
    private static backendRef?: DotNet.DotNetObject;
    private static hostKind?: HostKind;

    public static async init(backendRef: DotNet.DotNetObject, hostKind: HostKind): Promise<void> {
        // probably init can be called multiple times on MAUI
        debugLog?.log(`init`);
        this.backendRef = backendRef;
        this.hostKind = hostKind;

        if (hostKind === 'MauiApp')
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
        status.onchange ??= () => this.setPermissionState(status.state);
        return status.state;
    }

    /** Called by Blazor */
    // @ts-expect-error TODO: fix errors
    public static async getDeviceToken(): Promise<string | null>
    {
        let { firebaseApp, firebasePublicKey } = BrowserInit;
        try {
            if (!firebaseApp) {
                // @ts-expect-error TODO: fix errors
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

    /** Called by Blazor: asks the service worker to reconcile shown notifications against the
     *  server's active set — close ones no longer active, and (re)show newly-active ones that
     *  aren't shown (createTags), healing a lost dismissal push or a dropped delivery push. */
    public static async reconcileNotifications(active: ActiveNotificationInfo[], createTags: string[]): Promise<void> {
        try {
            const registration = await navigator.serviceWorker.getRegistration('sw.js');
            const sw = registration?.active;
            if (!sw)
                return;
            sw.postMessage({ type: 'RECONCILE_NOTIFICATIONS', active, createTags });
        }
        catch (error) {
            warnLog?.log(`reconcileNotifications: failed`, error);
        }
    }

    /** Called by Blazor */
    public static async deleteDeviceToken(): Promise<void> {
        const { firebaseApp } = BrowserInit;
        if (!firebaseApp)
            return;

        const messaging = getMessaging(firebaseApp);
        deleteToken(messaging);
        // @ts-expect-error TODO: fix errors
        BrowserInit.firebaseApp = null; // reset Firebase App registration
        // @ts-expect-error TODO: fix errors
        BrowserInit.firebaseAnalytics = null;
    }

    public static async registerRequestNotificationHandler(element: HTMLElement): Promise<void> {
        element.addEventListener('click', this.requestNotificationPermissionHandler);
    }

    public static async unregisterRequestNotificationHandler(element: HTMLElement): Promise<void> {
        element.removeEventListener('click', this.requestNotificationPermissionHandler);
    }

    // @ts-expect-error TODO: fix errors
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
        // @ts-expect-error TODO: fix errors
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
            // @ts-expect-error TODO: fix errors
            await this.backendRef.invokeMethodAsync('NavigateToNotificationUrl', url);
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
        // @ts-expect-error readonly property
        Notification['permission'] = permission;
    }
}

function hasPromiseBasedNotificationApi(): boolean {
    try {
        Notification.requestPermission().then();
        return true;
        // eslint-disable-next-line @typescript-eslint/no-unused-vars
    } catch(e) {
        return false;
    }
}

// Interactive.whenInteractive().then(async () => {
//     if (BrowserInfo.hostKind == 'Maui')
//         return;
//
//     const isGranted = await requestNotificationsPermission();
//     if (!isGranted)
//         errorLog?.log(`Notifications permission isn't granted`);
// });
