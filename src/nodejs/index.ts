// Styles
import './fonts/svgtofont/icon.css';
import './fonts/tt-commons-pro.css';
import './node_modules/fork-awesome/css/fork-awesome.min.css';
import './styles/index.css';

// Libraries
import { register } from 'swiper/element/bundle';
register();

// Logging init
import 'logging-init';
// FontSize & Theme init
import { FontSizes } from 'font-sizes';
import { Theme } from 'theme';
// Critical init logic - should go right after logging-init
import './src/init';

// Exports
import * as ui from '../dotnet/UI.Blazor/exports';
import * as blazorApp from '../dotnet/UI.Blazor.App/exports';
import { Kvas } from './src/kvas';

// Assign to window objects
window.ui = {
    ...ui,
};
window.FontSizes = FontSizes;
window.Theme = Theme;
window.blazorApp = blazorApp;
window.Kvas = Kvas;

// eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
window.App?.markBundleReady?.(); // "?." here ensures this code won't fail in workers, etc.
