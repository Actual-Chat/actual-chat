using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class ThemeUI : WorkerBase
{
    private readonly ISyncedState<ThemeSettings> _settings;
    private readonly TaskCompletionSource<Unit> _whenReadySource = TaskCompletionSourceExt.New<Unit>();
    private Theme _appliedTheme = Theme.Light;

    private ILogger Log { get; }
    private HostInfo HostInfo { get; }
    private Dispatcher Dispatcher { get; }
    private IJSRuntime JS { get; }

    public IState<ThemeSettings> Settings => _settings;
    public Theme Theme {
        get => _settings.Value.Theme;
        set => _settings.Value = new ThemeSettings(value);
    }
    public Task WhenReady => _whenReadySource.Task;

    public ThemeUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        HostInfo = services.GetRequiredService<HostInfo>();
        Dispatcher = services.GetRequiredService<Dispatcher>();
        JS = services.GetRequiredService<IJSRuntime>();

        var stateFactory = services.StateFactory();
        var accountSettings = services.AccountSettings().WithPrefix(nameof(ThemeUI));
        _settings = stateFactory.NewKvasSynced<ThemeSettings>(
            new(accountSettings, nameof(ThemeSettings)) {
                InitialValue = new ThemeSettings(Theme.Light),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);

        await foreach (var cTheme in Settings.Changes(cancellationToken).ConfigureAwait(false))
            await ApplyTheme(cTheme.Value.Theme);
    }

    private Task ApplyTheme(Theme theme)
        => Dispatcher.InvokeAsync(async () => {
            _whenReadySource.TrySetResult(default);
            if (!HostInfo.IsDevelopmentInstance) // Themes work on dev instances only
                return;
            if (_appliedTheme == theme)
                return;

            _appliedTheme = theme;
            try {
                await JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.ThemeUI.applyTheme", theme.ToString());
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, "Failed to apply the new theme");
            }
        });
}
