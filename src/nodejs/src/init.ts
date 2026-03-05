import { DeviceInfo } from 'device-info';
import { Interactive } from 'interactive';
import { Gestures } from 'gestures';
import { ServiceWorker } from 'service-worker';
import { BrowserInit } from '../../dotnet/UI.Blazor/Services/BrowserInit/browser-init';

DeviceInfo.updateBodyClasses();
Interactive.init();
Gestures.init();
void ServiceWorker.init();

void (async () => {
    if (window.visualViewport) {
        window.visualViewport.addEventListener('resize', () => {
            if (!window.visualViewport)
                return;

            const vh = window.visualViewport.height * 0.01;
            window.document.documentElement.style.setProperty('--vh', `${vh}px`);
        });
    }

    // Prevent body scrolling: some browsers (Safari) allow user to scroll up
    // a non-existent part of the document body that "hides" below the keyboard.
    // We do a few other steps to prevent this, so this one is quite unlikely
    // to be used, but... It doesn't make anything worse, so it stays here.
    window.addEventListener('scroll', e => {
        e.preventDefault();
        window.scrollTo(0, 0);
    });

    const app = window.App;
    if (app) {
        await app.whenBlazorReady;
        BrowserInit.startReloadWatchers();
        void BrowserInit.startWebSplashRemoval(5_000);
    }
})();
