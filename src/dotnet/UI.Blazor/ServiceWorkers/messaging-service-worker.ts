import { initializeApp } from 'firebase/app';
import { getMessaging, onBackgroundMessage } from 'firebase/messaging/sw';

fetch('/dist/config/firebase.config.json')
    .then((response) => response.json())
    .then((firebaseConfig) => {
        const app = initializeApp(firebaseConfig.config);
        const messaging = getMessaging(app);

        onBackgroundMessage(messaging, payload => {
            console.log('[messaging-service-worker.ts] Received background message ', payload);
        });
    });
