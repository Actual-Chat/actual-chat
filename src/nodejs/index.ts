// Styles
import './fonts/icon.css';
import './fonts/tt-commons-pro.css';
import './node_modules/fork-awesome/css/fork-awesome.min.css';
import './styles/tailwind.css';
import './styles/colors.css';
import './styles/scrollbars.css';
import './styles/spinkit.css';
import './styles/blazor.css';
import './styles/main.css';

// Logging init
import 'logging-init';

// Exports
export * as ui from '../dotnet/UI.Blazor/exports';
export * as audio from '../dotnet/Audio.UI.Blazor/exports';
export * as chat from '../dotnet/Chat.UI.Blazor/exports';
export * as users from '../dotnet/Users.UI.Blazor/exports';
export * as blazorApp from '../dotnet/UI.Blazor.App/exports';

// Initialization
import './src/init'
