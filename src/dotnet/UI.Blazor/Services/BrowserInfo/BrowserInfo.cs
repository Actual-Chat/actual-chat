using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class BrowserInfo : IBrowserInfoBackend, IOriginProvider, IDisposable
{
    private DotNetObjectReference<IBrowserInfoBackend>? _backendRef;
    private readonly IMutableState<ScreenSize> _screenSize;
    private readonly TaskSource<Unit> _whenReadySource;

    private IServiceProvider Services { get; }
    private IJSRuntime JS { get; }
    private ILogger Log { get; }
    private HostInfo HostInfo { get; }

    public AppKind AppKind { get; }
    public IState<ScreenSize> ScreenSize => _screenSize;
    public TimeSpan UtcOffset { get; private set; }
    public bool IsMobile { get; private set; }
    public bool IsAndroid { get; private set; }
    public bool IsIos { get; private set; }
    public bool IsChrome { get; private set; }
    public bool IsTouchCapable { get; private set; }
    public string WindowId { get; private set; } = "";
    public Task WhenReady => _whenReadySource.Task;
    string IOriginProvider.Origin => WindowId;

    public BrowserInfo(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        JS = services.GetRequiredService<IJSRuntime>();
        HostInfo = services.GetRequiredService<HostInfo>();
        AppKind = HostInfo.AppKind;

        _screenSize = services.StateFactory().NewMutable<ScreenSize>();
        _whenReadySource = TaskSource.New<Unit>(true);
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    public async Task Init()
    {
        _backendRef = DotNetObjectReference.Create<IBrowserInfoBackend>(this);
        await JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.BrowserInfo.init",
            _backendRef,
            AppKind.ToString());
    }

    [JSInvokable]
    public void OnInitialized(IBrowserInfoBackend.InitResult initResult) {
        // Log.LogInformation("Init: {InitResult}", initResult);
        SetScreenSize(initResult.ScreenSizeText);
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
    public void OnScreenSizeChanged(string screenSizeText)
        => SetScreenSize(screenSizeText);

    [JSInvokable]
    public void OnRedirect(string url)
        => Services.GetRequiredService<NavigationManager>().NavigateTo(url);

    private void SetScreenSize(string screenSizeText)
    {
        if (!Enum.TryParse<ScreenSize>(screenSizeText, true, out var screenSize))
            screenSize = Blazor.Services.ScreenSize.Unknown;
        // Log.LogInformation("ScreenSize = {ScreenSize}", screenSize);
        _screenSize.Value = screenSize;
    }
}
