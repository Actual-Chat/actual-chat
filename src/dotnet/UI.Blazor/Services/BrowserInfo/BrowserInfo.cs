using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class BrowserInfo : IBrowserInfoBackend, IDisposable
{
    private DotNetObjectReference<IBrowserInfoBackend>? _backendRef;
    private readonly IMutableState<ScreenSize> _screenSize;
    private readonly IMutableState<bool> _isHoverable;
    private readonly TaskSource<Unit> _whenReadySource;
    private readonly object _lock = new();

    private IServiceProvider Services { get; }
    private ILogger Log { get; }

    private HostInfo HostInfo { get; }
    private History History { get; }
    private UrlMapper UrlMapper { get; }
    private IJSRuntime JS { get; }
    private UICommander UICommander { get; }

    public AppKind AppKind { get; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<ScreenSize> ScreenSize => _screenSize;
    public IState<bool> IsHoverable => _isHoverable;
    public TimeSpan UtcOffset { get; private set; }
    public bool IsMobile { get; private set; }
    public bool IsAndroid { get; private set; }
    public bool IsIos { get; private set; }
    public bool IsChrome { get; private set; }
    public bool IsTouchCapable { get; private set; }
    public string WindowId { get; private set; } = "";
    public Task WhenReady => _whenReadySource.Task;

    public BrowserInfo(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        HostInfo = services.GetRequiredService<HostInfo>();
        History = services.GetRequiredService<History>();
        UrlMapper = services.GetRequiredService<UrlMapper>();
        JS = services.GetRequiredService<IJSRuntime>();
        UICommander = services.GetRequiredService<UICommander>();
        AppKind = HostInfo.AppKind;

        var stateFactory = services.StateFactory();
        _screenSize = stateFactory.NewMutable<ScreenSize>();
        _isHoverable = stateFactory.NewMutable(false);
        _whenReadySource = TaskSource.New<Unit>(true);
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    public async Task Initialize()
    {
        _backendRef = DotNetObjectReference.Create<IBrowserInfoBackend>(this);
        await JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.BrowserInfo.init",
            _backendRef,
            AppKind.ToString());
    }

    [JSInvokable]
    public void OnInitialized(IBrowserInfoBackend.InitResult initResult)
    {
        Log.LogDebug("OnInitialized: {InitResult}", initResult);
        using var _ = Services.Tracer().Region("BrowserInfo.OnInitialized");
        SetScreenSize(initResult.ScreenSizeText, initResult.IsHoverable);
        UtcOffset = TimeSpan.FromMinutes(initResult.UtcOffset);
        IsMobile = initResult.IsMobile;
        IsAndroid = initResult.IsAndroid;
        IsIos = initResult.IsIos;
        IsChrome = initResult.IsChrome;
        IsTouchCapable = initResult.IsTouchCapable;
        WindowId = initResult.WindowId;
        _whenReadySource.SetResult(default);
    }

    [JSInvokable]
    public void OnScreenSizeChanged(string screenSizeText, bool isHoverable)
        => SetScreenSize(screenSizeText, isHoverable);

    private void SetScreenSize(string screenSizeText, bool isHoverable)
    {
        if (!Enum.TryParse<ScreenSize>(screenSizeText, true, out var screenSize))
            screenSize = Blazor.Services.ScreenSize.Unknown;
        // Log.LogInformation("ScreenSize = {ScreenSize}", screenSize);

        lock (_lock) {
            if (_screenSize.Value == screenSize && _isHoverable.Value == isHoverable)
                return;

            _screenSize.Value = screenSize;
            _isHoverable.Value = isHoverable;
        }
        UICommander.RunNothing(); // To instantly update everything
    }
}
