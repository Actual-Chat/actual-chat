import { DeviceInfo } from 'device-info';
import { Interactive } from 'interactive';
import { Gestures } from 'gestures';
import { EmojiPreview } from 'emoji-preview';
import { ServerClock } from 'clocks';
import { SharedSettings } from 'shared-settings';
import { ServiceWorker } from 'service-worker';
import { ScreenOrientation, DeviceOrientation } from 'orientation';
import { CompactLayout } from 'compact-layout';
import { BrowserInit } from '../../dotnet/UI.Blazor/Services/BrowserInit/browser-init';

globalThis.ServerClock = ServerClock;
globalThis.SharedSettings = SharedSettings;
globalThis.EmojiPreview = EmojiPreview;
DeviceInfo.updateBodyClasses();
ScreenOrientation.init();
DeviceOrientation.init();
Interactive.init();
Gestures.init();
EmojiPreview.init();
void ServiceWorker.init();

void (async () => {
    if (window.visualViewport) {
        let vhRafId = 0;
        window.visualViewport.addEventListener('resize', () => {
            if (vhRafId !== 0)
                return;
            vhRafId = window.requestAnimationFrame(() => {
                vhRafId = 0;
                if (!window.visualViewport)
                    return;
                const vh = window.visualViewport.height * 0.01;
                window.document.documentElement.style.setProperty('--vh', `${vh}px`);
            });
        });
    }

    // Landscape mobile has too little vertical room for inline video + full header,
    // so we ask the chat layout to fold into compact mode while it lasts.
    if (DeviceInfo.isMobile) {
        const updateLandscapeCompact = () => {
            if (ScreenOrientation.isPortrait)
                CompactLayout.release('landscape-mobile');
            else
                CompactLayout.request('landscape-mobile');
        };
        ScreenOrientation.change$.subscribe(updateLandscapeCompact);
        updateLandscapeCompact();
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
