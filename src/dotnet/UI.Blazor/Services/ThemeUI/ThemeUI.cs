using ActualChat.Kvas;
using ActualChat.UI.Blazor.Module;
using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public enum Theme { Light, Dark }

public class ThemeUI : WorkerBase
{
    private Theme _lastTheme = default;
    private AsyncLock _asyncLock = new(ReentryMode.CheckedFail);

    private ILogger Log { get; }
    private Dispatcher Dispatcher { get; }
    private IJSRuntime JS { get; }

    public ISyncedState<Theme> Theme { get; }

    public ThemeUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Dispatcher = services.GetRequiredService<Dispatcher>();
        JS = services.GetRequiredService<IJSRuntime>();

        var stateFactory = services.StateFactory();
        var accountSettings = services.AccountSettings().WithPrefix(nameof(ThemeUI));
        Theme = stateFactory.NewKvasSynced<Theme>(
            new(accountSettings, nameof(Theme)) {
                InitialValue = default,
                Corrector = FixTheme,
            });
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        await foreach (var cTheme in Theme.Changes(FixedDelayer.ZeroUnsafe, cancellationToken).ConfigureAwait(false))
            await ApplyTheme(cTheme.Value);
    }

    public async ValueTask ApplyTheme(Theme theme)
    {
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        if (_lastTheme == theme)
            return;
        try {
            await Dispatcher.InvokeAsync(
                () => JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.ThemeUI.applyTheme", theme.ToString()).AsTask()
                ).ConfigureAwait(false);
            _lastTheme = theme;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Failed to apply the new theme");
        }
    }

    private ValueTask<Theme> FixTheme(Theme theme, CancellationToken cancellationToken)
        => ValueTask.FromResult(Enum.IsDefined(theme) ? theme : default);
}
