// Styles
import './fonts/icon.css';
import './fonts/tt-commons-pro.css';
import './node_modules/fork-awesome/css/fork-awesome.min.css';
import './styles/index.css';

// Logging init
import 'logging-init';
// Critical init logic - should go right after logging-init
import './src/init'

// Exports
export * as ui from '../dotnet/UI.Blazor/exports';
export * as audio from '../dotnet/Audio.UI.Blazor/exports';
export * as chat from '../dotnet/Chat.UI.Blazor/exports';
export * as blazorApp from '../dotnet/UI.Blazor.App/exports';
