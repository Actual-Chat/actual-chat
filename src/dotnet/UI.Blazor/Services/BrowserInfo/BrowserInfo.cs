using System.Diagnostics.CodeAnalysis;

namespace ActualChat.UI.Blazor.Services;

public class BrowserInfo : ScopedServiceBase<UIHub>, IBrowserInfoBackend
{
    private readonly MutableState<ScreenSize> _screenSize;
    private readonly MutableState<bool> _isHoverable;
    private readonly MutableState<bool> _isVisible;
    private readonly MutableState<ThemeInfo> _themeInfo;

    protected readonly TaskCompletionSource WhenReadySource = TaskCompletionSourceExt.New();
    protected readonly object Lock = new();

    protected UICommander UICommander => Hub.UICommander();

    public DotNetObjectReference<IBrowserInfoBackend> BlazorRef { get; private set; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<ScreenSize> ScreenSize => _screenSize;
    public IState<bool> IsHoverable => _isHoverable;
    public IState<bool> IsVisible => _isVisible;
    public IState<ThemeInfo> ThemeInfo => _themeInfo;
    public TimeSpan UtcOffset { get; protected set; }
    public string TimeZone { get; protected set; } = "";
    public bool IsMobile { get; protected set; }
    public bool IsAndroid { get; protected set; }
    public bool IsIos { get; protected set; }
    public bool IsChromium { get; protected set; }
    public bool IsEdge { get; protected set; }
    public bool IsWebKit { get; protected set; }
    public bool IsTouchCapable { get; protected set; }
    public string WindowId { get; protected set; } = "";
    public Task WhenReady => WhenReadySource.Task;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BrowserInfo))]
    public BrowserInfo(UIHub hub) : base(hub)
    {
        BlazorRef = DotNetObjectReference.Create<IBrowserInfoBackend>(this);
        var stateFactory = StateFactory;
        _screenSize = stateFactory.NewMutable<ScreenSize>();
        _isHoverable = stateFactory.NewMutable(false);
        _isVisible = stateFactory.NewMutable(true);
        _themeInfo = stateFactory.NewMutable(Blazor.Services.ThemeInfo.Default);
        Hub.RegisterDisposable(BlazorRef);
    }

    [JSInvokable]
    public virtual void OnInitialized(IBrowserInfoBackend.InitResult initResult)
    {
        Log.LogDebug("OnInitialized: {InitResult}", initResult);

        UpdateThemeInfo(initResult.ThemeInfo);
        var screenSize = TryParseScreenSize(initResult.ScreenSizeText) ?? Blazor.Services.ScreenSize.Unknown;
        Update(screenSize, initResult.IsHoverable, initResult.IsVisible);
        UtcOffset = TimeSpan.FromMinutes(initResult.UtcOffset);
        TimeZone = initResult.TimeZone;
        IsMobile = initResult.IsMobile;
        IsAndroid = initResult.IsAndroid;
        IsIos = initResult.IsIos;
        IsChromium = initResult.IsChromium;
        IsEdge = initResult.IsEdge;
        IsWebKit = initResult.IsWebKit;
        IsTouchCapable = initResult.IsTouchCapable;
        WindowId = initResult.WindowId;
        WhenReadySource.TrySetResult();
    }

    [JSInvokable]
    public void OnScreenSizeChanged(string screenSizeText, bool isHoverable)
    {
        if (!Enum.TryParse<ScreenSize>(screenSizeText, true, out var screenSize))
            screenSize = Blazor.Services.ScreenSize.Unknown;
        Update(screenSize, isHoverable);
    }

    [JSInvokable]
    public void OnIsVisibleChanged(bool isVisible)
        => Update(isVisible: isVisible);

    [JSInvokable]
    public void OnThemeChanged(IBrowserInfoBackend.ThemeInfo themeInfo)
        => UpdateThemeInfo(themeInfo);

    [JSInvokable]
    public virtual void OnWebSplashRemoved() { }

    // Protected & private methods

    protected void Update(ScreenSize? screenSize = null, bool? isHoverable = null, bool? isVisible = null)
    {
        var isUpdated = false;
        var becameVisible = false;
#pragma warning disable MA0064
        lock (Lock) {
#pragma warning restore MA0064
            if (screenSize is { } vScreenSize && _screenSize.Value != vScreenSize) {
                _screenSize.Value = vScreenSize;
                isUpdated = true;
            }
            if (isHoverable is { } vIsHoverable && _isHoverable.Value != vIsHoverable) {
                _isHoverable.Value = vIsHoverable;
                isUpdated = true;
            }
            if (isVisible is { } vIsVisible && _isVisible.Value != vIsVisible) {
                _isVisible.Value = vIsVisible;
                becameVisible = vIsVisible;
                isUpdated = true;
            }
        }
        if (isUpdated)
            _ = UICommander.RunNothing(); // To instantly update everything
        if (becameVisible)
            Services.GetRequiredService<ReconnectUI>().ResetReconnectDelays();
    }

    protected void UpdateThemeInfo(IBrowserInfoBackend.ThemeInfo themeInfo)
        => _themeInfo.Value = new(
            TryParseTheme(themeInfo.Theme),
            TryParseTheme(themeInfo.DefaultTheme) ?? Theme.Light,
            TryParseTheme(themeInfo.CurrentTheme) ?? Theme.Light,
            themeInfo.Colors);

    protected static ScreenSize? TryParseScreenSize(string? screenSize)
        => Enum.TryParse<ScreenSize>(screenSize ?? "", true, out var v) ? v : null;

    protected static Theme? TryParseTheme(string? theme)
        => Enum.TryParse<Theme>(theme ?? "", true, out var v) ? v : null;
}
