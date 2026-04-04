using ActualChat.Aot;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App;
using Microsoft.AspNetCore.Components.WebView;

namespace ActualChat.App.Maui.Module;

internal class MauiAppAotSource : IAotSource
{
    public void KeepTypes()
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        // App types (static types use string-based Keep)
        CodeKeeper.Keep(typeof(MauiProgram));
        CodeKeeper.Keep(typeof(MauiDiagnostics));
        CodeKeeper.Keep<MauiBlazorApp>();
        CodeKeeper.Keep<WebApp>();

        // WebView types
        CodeKeeper.Keep<MauiWebView>();
        CodeKeeper.Keep<WebViewManager>();

        // Services used via DI/reflection
        CodeKeeper.Keep<MauiBrowserInfo>();
        CodeKeeper.Keep<MauiSession>();
        CodeKeeper.Keep<MauiMicrophonePermissionHandler>();
        CodeKeeper.Keep<MauiContactsPermissionHandler>();
        CodeKeeper.Keep<MauiCameraPermissionHandler>();

        // Framework types needed for Blazor IPC
        CodeKeeper.Keep<Editor>();
        CodeKeeper.Keep("Microsoft.AspNetCore.Components.WebView.IpcCommon, Microsoft.AspNetCore.Components.WebView");
        CodeKeeper.Keep("Microsoft.AspNetCore.Components.WebView.IpcCommon+IncomingMessageType, Microsoft.AspNetCore.Components.WebView");
        CodeKeeper.Keep("Microsoft.AspNetCore.Components.WebView.IpcCommon+OutgoingMessageType, Microsoft.AspNetCore.Components.WebView");
        CodeKeeper.Keep("Microsoft.AspNetCore.Components.WebView.IpcSender, Microsoft.AspNetCore.Components.WebView");
        CodeKeeper.Keep("Microsoft.AspNetCore.Components.WebView.IpcReceiver, Microsoft.AspNetCore.Components.WebView");
        CodeKeeper.Keep("Microsoft.AspNetCore.Components.WebView.Maui.AndroidWebKitWebViewManager, Microsoft.AspNetCore.Components.WebView.Maui");

        // MAUI platform types
        CodeKeeper.Keep("Microsoft.Maui.Controls.Compatibility.Platform.UWP.WindowsResourcesProvider, Microsoft.Maui.Controls");

#if ANDROID
        CodeKeeper.Keep<AndroidJSInterface>();
#endif
    }

    public (Type, AotTypeKind)[] ListTypes() => [];
}
