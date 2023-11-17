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
export * from 'font-sizes';
export * from 'theme';
// Critical init logic - should go right after logging-init
import './src/init'

// Exports
export * as ui from '../dotnet/UI.Blazor/exports';
export * as audio from '../dotnet/Audio.UI.Blazor/exports';
export * as chat from '../dotnet/Chat.UI.Blazor/exports';
export * as blazorApp from '../dotnet/UI.Blazor.App/exports';
export * as notification from '../dotnet/Notification.UI.Blazor/exports';
export * from './src/kvas';

// eslint-disable-next-line @typescript-eslint/no-unsafe-call,@typescript-eslint/no-unsafe-member-access
globalThis?.App?.markBundleReady?.(); // "?." here ensures this code won't fail in workers, etc.
