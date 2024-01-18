using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public class MauiBrowserInfo : BrowserInfo
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiBrowserInfo))]
    public MauiBrowserInfo(UIHub hub)
        : base(hub)
    {
        var appKind = HostInfo.AppKind;
        var isWindowsOrMacOS = appKind is AppKind.Windows or AppKind.MacOS;
        var deviceIdiom = DeviceInfo.Current.Idiom;
        var isMobile = deviceIdiom == DeviceIdiom.Phone
            || deviceIdiom == DeviceIdiom.Tablet
            || deviceIdiom == DeviceIdiom.Watch;

        UtcOffset = TimeZoneInfo.Local.BaseUtcOffset;
        IsMobile = isMobile && !isWindowsOrMacOS;
        IsAndroid = appKind == AppKind.Android;
        IsIos = appKind == AppKind.Ios;
        IsEdge = appKind == AppKind.Windows;
        IsChromium = IsAndroid || IsEdge; // IsEdge is needed for MAUI on Windows
        IsWebKit = IsIos || appKind == AppKind.MacOS;
        IsTouchCapable = isMobile;

        var display = DeviceDisplay.Current.MainDisplayInfo;
        var isWide = !isMobile || display.Orientation == DisplayOrientation.Landscape;
        var screenSize = isWide ? UI.Blazor.Services.ScreenSize.Medium : UI.Blazor.Services.ScreenSize.Small;
        Update(screenSize, !isMobile, false);
    }

    [JSInvokable]
    public override void OnInitialized(IBrowserInfoBackend.InitResult initResult)
    {
        Log.LogDebug("OnInitialized: {InitResult}", initResult);

        UpdateThemeInfo(initResult.ThemeInfo);
        var screenSize = TryParseScreenSize(initResult.ScreenSizeText) ?? UI.Blazor.Services.ScreenSize.Unknown;
        Update(screenSize, initResult.IsHoverable, initResult.IsVisible);
        WindowId = initResult.WindowId;
        // We don't want to change any other properties here

        WhenReadySource.TrySetResult();
        MauiLoadingUI.MarkFirstWebViewCreated();
        MauiThemeHandler.Instance.Apply();
    }

    [JSInvokable]
    public override void OnWebSplashRemoved()
    {
        MauiLoadingUI.MarkFirstSplashRemoved();
        MauiThemeHandler.Instance.Apply();
    }
}
