import './node_modules/fork-awesome/css/fork-awesome.min.css';
import './styles/tailwind.css';
import './styles/colors.css';
import './styles/scrollbars.css';
import './styles/spinkit.css';
import './styles/blazor.css';
import './styles/main.css';

import './src/swipe-events.js';
import './node_modules/long-press-event/dist/long-press-event.min.js'

export * as core from '../dotnet/UI.Blazor/exports';
export * as audio from '../dotnet/Audio.UI.Blazor/exports';
export * as chat from '../dotnet/Chat.UI.Blazor/exports';
export * as users from '../dotnet/Users.UI.Blazor/exports';

// Initialization

import './src/init'
