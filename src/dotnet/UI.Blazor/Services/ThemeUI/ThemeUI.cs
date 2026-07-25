namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages light/dark theme switching and applies theme changes to registered handlers.
/// </summary>
public class ThemeUI(UIHub hub) : UIWorkerBase<UIHub>(hub)
{
    private static readonly string JSThemeClassName = "window.Theme";
    private static readonly string JSSetMethod = $"{JSThemeClassName}.set";

    private Theme _lightTheme = Theme.Light;
    private BrowserInfo BrowserInfo => field ??= Services.GetRequiredService<BrowserInfo>();
    private IEnumerable<Action<ThemeInfo>> ThemeHandlers =>
        field ??= Services.GetRequiredService<IEnumerable<Action<ThemeInfo>>>();

    public IState<ThemeInfo> State => BrowserInfo.ThemeInfo;
    public Task WhenReady => BrowserInfo.WhenReady;

    public ValueTask SetTheme(Theme? theme)
    {
        var sTheme = theme?.ToString().ToLower();
        return JS.InvokeVoidAsync(JSSetMethod, sTheme);
    }

    public ValueTask ToggleTheme()
    {
        // CurrentTheme is the effective theme, so "system" resolves to what's actually shown;
        // _lightTheme makes the toggle return to Ash rather than Light when it started from Ash.
        var currentTheme = State.Value.CurrentTheme;
        if (currentTheme != Theme.Dark)
            _lightTheme = currentTheme;
        return SetTheme(currentTheme == Theme.Dark ? _lightTheme : Theme.Dark);
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
