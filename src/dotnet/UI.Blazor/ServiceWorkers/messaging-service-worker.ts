import { initializeApp } from 'firebase/app';
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw';

const configBase64 = new URL(location.href).searchParams.get('config');
const configString = atob(configBase64);
const config = JSON.parse(configString);

const app = initializeApp(config);
const messaging = getMessaging(app);
onBackgroundMessage(messaging, payload => {
    console.log('[messaging-service-worker.ts] Received background message ', payload);
    // payload.
});

