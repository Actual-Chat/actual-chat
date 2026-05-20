// Styles
import './fonts/svgtofont/icon.css';
import './fonts/tt-commons-pro.css';
import 'fork-awesome/css/fork-awesome.min.css';
import './styles/index.css';

// Libraries
import { register } from 'swiper/element/bundle';
register();

// Logging init — touching the module triggers @actuallab/core's
// initLogging() (lazy on first Log.get) and registers the global controller.
import 'logging';
// FontSize & Theme init
import { FontSizes } from 'font-sizes';
import { Theme } from 'theme';
// Critical init logic - should go right after logging-init
import './src/init';

// Exports
import * as ui from '../dotnet/UI.Blazor/exports';
import * as blazorApp from '../dotnet/UI.Blazor.App/exports';
import { Kvas } from 'kvas';

declare global {
    interface Window {
        ui: typeof ui;
        FontSizes: typeof FontSizes;
        Theme: typeof Theme;
        blazorApp: typeof blazorApp;
        Kvas: typeof Kvas;
        App?: {
            renderMode?: string;
            whenBlazorReady?: Promise<void>;
            markBundleReady?(): void;
            markBlazorReady?(): void;
        };
    }
}

// Assign to window objects
window.ui = {
    ...ui,
};
window.FontSizes = FontSizes;
window.Theme = Theme;
window.blazorApp = blazorApp;
window.Kvas = Kvas;

blazorApp.initFpsOverlay();
blazorApp.initChatViewScroll();
ui.initKeyboardUI();

window.App?.markBundleReady?.(); // "?." here ensures this code won't fail in workers, etc.
