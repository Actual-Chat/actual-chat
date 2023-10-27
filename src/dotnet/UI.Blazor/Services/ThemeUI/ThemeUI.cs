using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class ThemeUI : WorkerBase
{
    private const Theme DefaultTheme = Theme.Light;
    private readonly ISyncedState<ThemeSettings> _settings;
    private readonly TaskCompletionSource _whenReadySource = TaskCompletionSourceExt.New();
    // Nearly every service here is requested only when the theme is applied,
    // so it makes sense to postpone their resolution
    private Dispatcher? _dispatcher;
    private IJSRuntime? _js;
    private ILogger? _log;

    private Theme _appliedTheme = DefaultTheme;

    private IServiceProvider Services { get; }
    private Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();
    private IJSRuntime JS => _js ??= Services.JSRuntime();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public IState<ThemeSettings> Settings => _settings;
    public Theme Theme {
        get => _settings.Value.Theme.GetValueOrDefault(DefaultTheme);
        set => _settings.Value = new ThemeSettings(value);
    }
    public Task WhenReady => _whenReadySource.Task;

    public ThemeUI(IServiceProvider services)
    {
        Services = services;

        var stateFactory = services.StateFactory();
        var accountSettings = services.LocalSettings().WithPrefix(nameof(ThemeUI));
        _settings = stateFactory.NewKvasSynced<ThemeSettings>(
            new(accountSettings, nameof(ThemeSettings)) {
                InitialValue = new ThemeSettings(null),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
        Log.LogInformation("State created");
    }

    protected override Task DisposeAsyncCore()
    {
        _settings.Dispose();
        return base.DisposeAsyncCore();
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        Log.LogInformation("Worker started");
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);
        Log.LogInformation("State first time read");

        await foreach (var cTheme in Settings.Changes(cancellationToken).ConfigureAwait(false)) {
            Log.LogInformation("State change. Theme: '{Theme}'", cTheme.Value.Theme);
            await ApplyTheme(cTheme.Value.Theme.GetValueOrDefault(DefaultTheme)).ConfigureAwait(false);
        }
    }

    private Task ApplyTheme(Theme theme)
        => Dispatcher.InvokeAsync(async () => {
            _whenReadySource.TrySetResult();
            if (_appliedTheme == theme)
                return;

            var oldTheme = _appliedTheme;
            _appliedTheme = theme;
            try {
                // Ideally we don't want to use any external JS here,
                // coz this code may start before BulkInitUI completes
                var script = $"""
                document.body.classList.remove('{oldTheme.ToCssClass()}');
                document.body.classList.add('{theme.ToCssClass()}');
                """;
                await JS.EvalVoid(script).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, "Failed to apply the new theme");
            }
        });
}
