using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class BrowserInfo : IBrowserInfoBackend, IDisposable
{
    private DotNetObjectReference<IBrowserInfoBackend>? _backendRef;
    private readonly IMutableState<ScreenSize> _screenSize;
    private readonly TaskSource<Unit> _whenReadySource;

    private IServiceProvider Services { get; }
    private IJSRuntime JS { get; }
    private ILogger Log { get; }
    private HostInfo HostInfo { get; }

    public IState<ScreenSize> ScreenSize => _screenSize;
    public TimeSpan UtcOffset { get; private set; }
    public bool IsMobile { get; private set; }
    public bool IsTouchCapable { get; private set; }
    public bool IsMaui { get; private set; }
    public string WindowId { get; private set; } = "";
    public Task WhenReady => _whenReadySource.Task;

    public BrowserInfo(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        JS = services.GetRequiredService<IJSRuntime>();
        HostInfo = services.GetRequiredService<HostInfo>();
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
            HostInfo.AppKind == AppKind.Maui);
    }

    [JSInvokable]
    public void OnInitialized(IBrowserInfoBackend.InitResult initResult) {
        // Log.LogInformation("Init: {InitResult}", initResult);
        SetScreenSize(initResult.ScreenSizeText);
        UtcOffset = TimeSpan.FromMinutes(initResult.UtcOffset);
        IsMobile = initResult.IsMobile;
        IsTouchCapable = initResult.IsTouchCapable;
        IsMaui = initResult.IsMaui;
        WindowId = initResult.WindowId;
        _whenReadySource.SetResult(default);
    }

    [JSInvokable]
    public void OnScreenSizeChanged(string screenSizeText)
        => SetScreenSize(screenSizeText);

    private void SetScreenSize(string screenSizeText)
    {
        if (!Enum.TryParse<ScreenSize>(screenSizeText, true, out var screenSize))
            screenSize = Blazor.Services.ScreenSize.Unknown;
        // Log.LogInformation("ScreenSize = {ScreenSize}", screenSize);
        _screenSize.Value = screenSize;
    }
}
