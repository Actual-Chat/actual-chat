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
        // JavaScript bridge type invoked from WebView JS via JNI.
        CodeKeeper.Keep<AndroidJSInterface>();

        // Android components (Activity / Application / Service / BroadcastReceiver) are
        // instantiated by the OS through Java reflection. Each needs its default ctor and
        // overridden lifecycle methods preserved — the managed linker normally keeps them via
        // [Register]-style attributes, but under stricter AOT we pin them explicitly.
        CodeKeeper.Keep<MainActivity>();
        CodeKeeper.Keep<MainApplication>();
        CodeKeeper.Keep<AlarmReceiver>();
        CodeKeeper.Keep<ActualChat.App.Maui.Activities.AndroidActivitiesForegroundService>();
        CodeKeeper.Keep<FirebaseMessagingService>();
#endif

#if IOS || MACCATALYST
        // ObjC runtime resolves types by their [Register] name and calls exported selectors
        // against them. Under NativeAOT we pin the delegate types explicitly — Xamarin's
        // linker usually preserves them, but belt-and-braces for AOT.
        CodeKeeper.Keep<AppDelegate>();
#endif

#if WINDOWS
        // NAudio is preserved wholesale via <TrimmerRootAssembly Include="NAudio.Wasapi" />
        // in App.Maui.csproj (Windows Native AOT path) because ILC otherwise emits invalid
        // bodies for the ComObject wrapper ctors. CodeKeeper alone is insufficient — the
        // types have no generic instantiations we could exercise here. If new closed-generic
        // NAudio instantiations show up, pin them below.

        // WinRT StartupTask API: ensure both the public projection types and the CsWinRT-
        // generated ABI stubs are kept so that StartupTask.GetAsync / RequestEnableAsync /
        // Disable resolve at runtime. The actual call site hits ABI.*StaticsMethods, see
        // Platforms/Windows/WindowsAppSettings.cs.
        CodeKeeper.Keep("Windows.ApplicationModel.StartupTask, Microsoft.Windows.SDK.NET");
        CodeKeeper.Keep("Windows.ApplicationModel.IStartupTaskStatics, Microsoft.Windows.SDK.NET");
        CodeKeeper.Keep("Windows.ApplicationModel.StartupTaskState, Microsoft.Windows.SDK.NET");
        CodeKeeper.Keep("ABI.Windows.ApplicationModel.IStartupTaskStatics, Microsoft.Windows.SDK.NET");
        CodeKeeper.Keep("ABI.Windows.ApplicationModel.IStartupTaskStaticsMethods, Microsoft.Windows.SDK.NET");
        CodeKeeper.Keep("ABI.Windows.ApplicationModel.StartupTask, Microsoft.Windows.SDK.NET");
        CodeKeeper.Keep("WinRT.ExceptionHelpers, WinRT.Runtime");
        CodeKeeper.Keep("WinRT.IObjectReference, WinRT.Runtime");
#endif
    }

    public (Type, AotTypeKind)[] ListTypes() => [];
}
