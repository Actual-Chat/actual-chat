using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class BrowserInfo : IBrowserInfoBackend
{
    private readonly TaskSource<Unit> _whenReadySource;

    private IServiceProvider Services { get; }
    private IJSRuntime JS { get; }
    private ILogger Log { get; }

    public IMutableState<ScreenSize> ScreenSize { get; }
    public TimeSpan UtcOffset { get; private set; }
    public bool IsTouchCapable { get; private set; }
    public string WindowId { get; private set; } = "";
    public Task WhenReady => _whenReadySource.Task;

    public BrowserInfo(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        JS = Services.GetRequiredService<IJSRuntime>();
        ScreenSize = services.StateFactory().NewMutable<ScreenSize>();
        _whenReadySource = TaskSource.New<Unit>(true);
    }

    public async Task Init()
    {
        var backendRef = DotNetObjectReference.Create<IBrowserInfoBackend>(this);
        var initResult = await JS.InvokeAsync<IBrowserInfoBackend.InitResult>(
            $"{BlazorUICoreModule.ImportName}.BrowserInfo.init",
            backendRef);
        // Log.LogInformation("Init: {InitResult}", initResult);
        if (!Enum.TryParse<ScreenSize>(initResult.ScreenSizeText, true, out var screenSize))
            screenSize = Blazor.Services.ScreenSize.Unknown;
        ScreenSize.Value = screenSize;
        IsTouchCapable = initResult.IsTouchCapable;
        UtcOffset = TimeSpan.FromMinutes(initResult.UtcOffset);
        WindowId = initResult.WindowId;
        _whenReadySource.SetResult(default);
    }

    [JSInvokable]
    public void OnScreenSizeChanged(string screenSizeText) {
        if (!Enum.TryParse<ScreenSize>(screenSizeText, true, out var screenSize))
            screenSize = Blazor.Services.ScreenSize.Unknown;
        // Log.LogInformation("ScreenSize = {ScreenSize}", screenSize);
        ScreenSize.Value = screenSize;
    }
}
