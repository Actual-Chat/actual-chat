namespace ActualChat.UI.Blazor.Services;

public class ThemeUI : WorkerBase
{
    private static readonly string JSThemeClassName = "Theme";
    private static readonly string JSSetMethod = $"{JSThemeClassName}.set";

    private IEnumerable<Action<Theme, string>>? _themeHandlers;
    private BrowserInfo? _browserInfo;
    private IJSRuntime? _js;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private BrowserInfo BrowserInfo => _browserInfo ??= Services.GetRequiredService<BrowserInfo>();
    private IEnumerable<Action<Theme, string>> ThemeHandlers =>
        _themeHandlers ??= Services.GetRequiredService<IEnumerable<Action<Theme, string>>>();
    private IJSRuntime JS => _js ??= Services.JSRuntime();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public IMutableState<Theme?> Theme => BrowserInfo.Theme;
    public IState<Theme> DefaultTheme => BrowserInfo.DefaultTheme;
    public IState<(Theme? Theme, Theme FinalTheme)> ComputedTheme { get; }
    public Task WhenReady => ComputedTheme.WhenSynchronized();

    public ThemeUI(IServiceProvider services)
    {
        Services = services;
        ComputedTheme = services.StateFactory().NewComputed<(Theme?, Theme)>(new () {
            UpdateDelayer = FixedDelayer.Instant,
            Category = StateCategories.Get(GetType(), nameof(ComputedTheme)),
        }, async (_, cancellationToken) => {
            await BrowserInfo.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            var theme = await Theme.Use(cancellationToken).ConfigureAwait(false);
            var defaultTheme = await DefaultTheme.Use(cancellationToken).ConfigureAwait(false);
            return (theme, theme ?? defaultTheme);
        });
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        await ComputedTheme.WhenSynchronized(cancellationToken).ConfigureAwait(false);

        var lastTheme = ComputedTheme.Value;
        var (theme, finalTheme) = lastTheme;
        await ApplyTheme(theme, finalTheme).ConfigureAwait(false);

        await foreach (var cTheme in ComputedTheme.Changes(cancellationToken).ConfigureAwait(false)) {
            (theme, finalTheme) = cTheme.Value;
            if ((theme, finalTheme) == lastTheme)
                continue;

            Log.LogInformation("Theme changed: ({Theme}, {AppliedTheme})", theme, finalTheme);
            lastTheme = (theme, finalTheme);
            await ApplyTheme(theme, finalTheme).ConfigureAwait(false);
        }
    }

    private async Task ApplyTheme(Theme? theme, Theme finalTheme)
    {
        try {
            var colors = await JS
                .InvokeAsync<string>(JSSetMethod, theme?.ToString().ToLowerInvariant())
                .ConfigureAwait(false);
            InvokeThemeHandlers(finalTheme, colors);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Failed to apply theme");
        }
    }

    private void InvokeThemeHandlers(Theme theme, string colors)
    {
        foreach (var handler in ThemeHandlers)
            try {
                handler.Invoke(theme, colors);
            }
            catch (Exception e) {
                Log.LogError(e, "Theme handler of type {Type} failed", handler.GetType().GetName());
            }
    }
}
