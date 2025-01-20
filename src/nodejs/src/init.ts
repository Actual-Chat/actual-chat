import { DeviceInfo } from 'device-info';
import { Interactive } from 'interactive';
import { Gestures } from 'gestures';
import { ServiceWorker } from 'service-worker';
import { areWasmResourcesLikelyCached } from 'resources';
import { BrowserInit } from '../../dotnet/UI.Blazor/Services/BrowserInit/browser-init';

DeviceInfo.init();
Interactive.init();
Gestures.init();
void ServiceWorker.init();

void (async () => {
    if (window?.visualViewport) {
        window.visualViewport.addEventListener('resize', () => {
            const vh = window.visualViewport.height * 0.01;
            window.document.body.style.setProperty('--vh', `${vh}px`);
        });
    }

    if (DeviceInfo.isIos) {
        window?.addEventListener('scroll', e => {
            e.preventDefault();
            window.scrollTo(0, 0);
        });
    }

    const app = window['App'] as {
        whenBlazorReady: Promise<void>,
        renderMode: string,
    };
    if (app) {
        await app.whenBlazorReady;
        BrowserInit.startReloadWatchers();
        void BrowserInit.startWebSplashRemoval(5_000);
        if (app.renderMode === 'a' && !areWasmResourcesLikelyCached())
            BrowserInit.showWebSplash();
    }
})();
