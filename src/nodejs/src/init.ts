import { DeviceInfo } from "device-info";
import { Interactive } from 'interactive';
import { Gestures } from 'gestures';
import { ServiceWorker } from 'service-worker';

DeviceInfo.init();
Interactive.init();
Gestures.init();
void ServiceWorker.init();

if (window.visualViewport) {
    window.visualViewport.addEventListener('resize', () => {
        const vh = window.visualViewport.height * 0.01;
        document.documentElement.style.setProperty('--vh', `${vh}px`);
    });
}
