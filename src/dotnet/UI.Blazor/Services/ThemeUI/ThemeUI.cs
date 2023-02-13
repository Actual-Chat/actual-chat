using ActualChat.Kvas;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class ThemeUI : WorkerBase
{
    private readonly ISyncedState<ThemeSettings> _settings;
    private Theme _appliedTheme = Theme.Light;

    private ILogger Log { get; }
    private Dispatcher Dispatcher { get; }
    private IJSRuntime JS { get; }
    private ITraceSession Trace { get; }

    public IState<ThemeSettings> Settings => _settings;
    public Theme Theme {
        get => _settings.Value.Theme;
        set => _settings.Value = new ThemeSettings(value);
    }
    public Task WhenReady { get; }

    public ThemeUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Dispatcher = services.GetRequiredService<Dispatcher>();
        JS = services.GetRequiredService<IJSRuntime>();
        Trace = services.GetRequiredService<ITraceSession>();

        var stateFactory = services.StateFactory();
        var accountSettings = services.AccountSettings().WithPrefix(nameof(ThemeUI));
        _settings = stateFactory.NewKvasSynced<ThemeSettings>(
            new(accountSettings, nameof(ThemeSettings)) {
                InitialValue = new ThemeSettings(Theme.Light),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
        WhenReady = TaskSource.New<Unit>(true).Task;
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        Trace.Track($"ThemeUI.RunInternal");
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);
        Trace.Track("ThemeUI.WhenFirstTimeRead");
        await foreach (var cTheme in Settings.Changes(cancellationToken).ConfigureAwait(false))
            await ApplyTheme(cTheme.Value.Theme);
    }

    private Task ApplyTheme(Theme theme)
    {
        Trace.Track($"ThemeUI.ApplyTheme. Theme: '{theme}'");
        return Dispatcher.InvokeAsync(async () => {
            Trace.Track($"ThemeUI.ApplyTheme.InvokeAsync Theme: '{theme}'");
            if (!WhenReady.IsCompleted)
                TaskSource.For((Task<Unit>)WhenReady).TrySetResult(default);
            if (_appliedTheme == theme)
                return;

            _appliedTheme = theme;
            try {
                var step = Trace.TrackStep($"ThemeUI.ApplyTheme in JS'");
                await JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.ThemeUI.applyTheme", theme.ToString());
                step.Complete();
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, "Failed to apply the new theme");
            }
        });
    }
}
