import { AnimationSweeper } from 'animation-sync';
import { RenderSync } from 'render-sync';
import { DeviceInfo } from 'device-info';
import { Interactive } from 'interactive';
import { Gestures } from 'gestures';
import { EmojiPreview } from 'emoji-preview';
import { ServerClock } from 'clocks';
import { SharedSettings } from 'shared-settings';
import { ServiceWorker } from 'service-worker';
import { ScreenOrientation, DeviceOrientation } from 'orientation';
import { CompactLayout } from 'compact-layout';
import { MutationProcessor } from 'mutation-processor';
import { initWebCodecsPolyfill } from 'webcodecs-polyfill-settings';
import { AppRecovery } from '../../dotnet/UI.Blazor/Services/AppRecovery/app-recovery';
import { BrowserInit } from '../../dotnet/UI.Blazor/Services/BrowserInit/browser-init';
import '../../dotnet/UI.Blazor.App/Services/app-presence-classes';
import '../../dotnet/UI.Blazor.App/Services/conversation-collapse';

// Before anything awaits: batches arriving until this runs are applied the old way.
// The hook name is published because "it silently did nothing" is the failure mode here -
// the host global it needs is platform-specific, and a miss is otherwise invisible.
// RenderSync itself is published so deferral can be switched on mid-call over the debugger,
// which is the only way to A/B it against the same call rather than a different one.
globalThis.renderSyncHook = RenderSync.init();
AppRecovery.startRenderBatchWatch();
globalThis.RenderSync = RenderSync;

globalThis.ServerClock = ServerClock;
globalThis.SharedSettings = SharedSettings;
globalThis.EmojiPreview = EmojiPreview;
DeviceInfo.updateBodyClasses();
// Before any worker starts: SharedSettingsWorkerSync pushes the current snapshot
// on register, so a worker created later never sees a realm without a level.
initWebCodecsPolyfill();
ScreenOrientation.init();
DeviceOrientation.init();
Interactive.init();
Gestures.init();
EmojiPreview.init();
void ServiceWorker.init();

void (async () => {
    if (window.visualViewport) {
        let vhRafId = 0;
        const updateViewportVars = () => {
            if (vhRafId !== 0)
                return;

            vhRafId = window.requestAnimationFrame(() => {
                vhRafId = 0;
                const viewport = window.visualViewport;
                if (!viewport)
                    return;

                const style = window.document.documentElement.style;
                style.setProperty('--vh', `${viewport.height * 0.01}px`);
                style.setProperty('--vv-top', `${viewport.offsetTop}px`);
            });
        };
        window.visualViewport.addEventListener('resize', updateViewportVars);
        // The keyboard panning the visual viewport changes offsetTop, and that fires scroll, not resize
        window.visualViewport.addEventListener('scroll', updateViewportVars);
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

    AnimationSweeper.start();
    MutationProcessor.start();
    // Published so the class-vs-:has() cost can be A/B'd inside one call over the debugger.
    globalThis.MutationProcessor = MutationProcessor;

    const app = window.App;
    if (app) {
        await app.whenBlazorReady;
        AppRecovery.start();
        void BrowserInit.startWebSplashRemoval(5_000);
    }
})();
