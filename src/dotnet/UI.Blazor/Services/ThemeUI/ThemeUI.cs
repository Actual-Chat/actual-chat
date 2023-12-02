namespace ActualChat.UI.Blazor.Services;

public class ThemeUI(IServiceProvider services) : ScopedWorkerBase(services)
{
    private static readonly string JSThemeClassName = "window.Theme";
    private static readonly string JSSetMethod = $"{JSThemeClassName}.set";

    private IEnumerable<Action<ThemeInfo>>? _themeHandlers;
    private BrowserInfo? _browserInfo;
    private IJSRuntime? _js;

    private BrowserInfo BrowserInfo => _browserInfo ??= Services.GetRequiredService<BrowserInfo>();
    private IEnumerable<Action<ThemeInfo>> ThemeHandlers =>
        _themeHandlers ??= Services.GetRequiredService<IEnumerable<Action<ThemeInfo>>>();
    private IJSRuntime JS => _js ??= Services.JSRuntime();

    public IState<ThemeInfo> State => BrowserInfo.ThemeInfo;
    public Task WhenReady => BrowserInfo.WhenReady;

    public ValueTask SetTheme(Theme? theme)
    {
        var sTheme = theme?.ToString().ToLowerInvariant();
        return JS.InvokeVoidAsync(JSSetMethod, sTheme);
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        var lastState = State.Value;
        ApplyTheme(lastState);
        await foreach (var (state, _) in State.Changes(cancellationToken).ConfigureAwait(false)) {
            if (state == lastState)
                continue;

            Log.LogInformation("Theme changed: {Theme}", state);
            ApplyTheme(state);
            lastState = state;
        }
    }

    private void ApplyTheme(ThemeInfo themeInfo)
    {
        foreach (var handler in ThemeHandlers)
            try {
                handler.Invoke(themeInfo);
            }
            catch (Exception e) {
                Log.LogError(e, "Theme handler of type {Type} failed", handler.GetType().GetName());
            }
    }
}
