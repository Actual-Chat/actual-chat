using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public enum Theme { Light, Dark }

public class ThemeUI : WorkerBase
{
    private readonly ILogger _log;
    private readonly AppBlazorCircuitContext _circuitContext;
    private readonly IJSRuntime _js;
    private Theme _appliedTheme;

    public IMutableState<Theme> CurrentTheme { get; }

    public ThemeUI(
        AppBlazorCircuitContext circuitContext,
        IJSRuntime js,
        IStateFactory stateFactory,
        ILogger<ThemeUI>? log = null)
    {
        _log = log ?? NullLogger<ThemeUI>.Instance;
        _circuitContext = circuitContext;
        _js = js;
        CurrentTheme = stateFactory.NewMutable<Theme>();
        Start();
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        await foreach (var cTheme in CurrentTheme.Changes(cancellationToken).ConfigureAwait(false))
            _ = BackgroundTask.Run(
                () => ApplyTheme(cTheme.Value, cancellationToken),
                _log, "Failed to apply new theme",
                cancellationToken);
    }

    private Task ApplyTheme(Theme theme, CancellationToken cancellationToken)
        => _circuitContext.Dispatcher.InvokeAsync(async () => {
            if (_appliedTheme == theme)
                return;
            _appliedTheme = theme;
            await _js.InvokeVoidAsync(
                $"{BlazorUICoreModule.ImportName}.setTheme",
                cancellationToken,
                theme.ToString()
            ).ConfigureAwait(false);
        });
}
