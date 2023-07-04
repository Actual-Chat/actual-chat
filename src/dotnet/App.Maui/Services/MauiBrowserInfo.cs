using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public class MauiBrowserInfo : BrowserInfo
{
    public MauiBrowserInfo(IServiceProvider services) : base(services) { }

    public override ValueTask Initialize(List<object?>? initCalls = null)
    {
        var clientKind = HostInfo.ClientKind;
        var isWindowsOrMacOS = clientKind is ClientKind.Windows or ClientKind.MacOS;
        var deviceIdiom = DeviceInfo.Current.Idiom;
        var isMobile = deviceIdiom == DeviceIdiom.Phone
            || deviceIdiom == DeviceIdiom.Tablet
            || deviceIdiom == DeviceIdiom.Watch;

        UtcOffset = TimeZoneInfo.Local.BaseUtcOffset;
        IsMobile = isMobile && !isWindowsOrMacOS;
        IsAndroid = clientKind == ClientKind.Android;
        IsIos = clientKind == ClientKind.Ios;
        IsEdge = clientKind == ClientKind.Windows;
        IsChromium = IsAndroid || IsEdge; // IsEdge is needed for MAUI on Windows
        IsWebKit = IsIos || clientKind == ClientKind.MacOS;
        IsTouchCapable = isMobile;

        var display = DeviceDisplay.Current.MainDisplayInfo;
        var isWide = !isMobile || display.Orientation == DisplayOrientation.Landscape;
        var screenSize = isWide ? UI.Blazor.Services.ScreenSize.Medium : UI.Blazor.Services.ScreenSize.Small;
        Update(screenSize, !isMobile, false);

        WhenReadySource.TrySetResult();
        return base.Initialize(initCalls);
    }

    [JSInvokable]
    public override void OnInitialized(IBrowserInfoBackend.InitResult initResult)
    {
        Log.LogDebug("OnInitialized: {InitResult}", initResult);

        if (!Enum.TryParse<ScreenSize>(initResult.ScreenSizeText, true, out var screenSize))
            screenSize = UI.Blazor.Services.ScreenSize.Unknown;

        Update(screenSize, initResult.IsHoverable, initResult.IsVisible);
        WindowId = initResult.WindowId;
        // We don't want to change any other properties here

        WhenReadySource.TrySetResult();
    }
}
