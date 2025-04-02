namespace ActualChat.UI.Blazor.Services;

public class ThemeUI(UIHub hub) : ScopedWorkerBase<UIHub>(hub)
{
    private static readonly string JSThemeClassName = "window.Theme";
    private static readonly string JSSetMethod = $"{JSThemeClassName}.set";

    [field: AllowNull, MaybeNull]
    private BrowserInfo BrowserInfo => field ??= Services.GetRequiredService<BrowserInfo>();
    [field: AllowNull, MaybeNull]
    private IEnumerable<Action<ThemeInfo>> ThemeHandlers =>
        field ??= Services.GetRequiredService<IEnumerable<Action<ThemeInfo>>>();
    [field: AllowNull, MaybeNull]
    private IJSRuntime JS => field ??= Services.JSRuntime();

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
        await foreach (var (state, _) in State.Computed.Changes(cancellationToken).ConfigureAwait(false)) {
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
