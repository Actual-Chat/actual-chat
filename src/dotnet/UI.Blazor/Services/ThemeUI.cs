using ActualChat.UI.Blazor.Module;
using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public enum Theme { Light, Dark }

public class ThemeUI : WorkerBase
{
    private readonly IAsyncLock _applyLock;
    private readonly IJSRuntime _js;
    private Theme _appliedTheme;

    public IMutableState<Theme> CurrentTheme { get; }

    public ThemeUI(IJSRuntime js, IStateFactory stateFactory)
    {
        _applyLock = new AsyncLock(ReentryMode.UncheckedDeadlock);
        _js = js;
        CurrentTheme = stateFactory.NewMutable<Theme>();
        Start();
    }

    protected override Task RunInternal(CancellationToken cancellationToken)
        => CurrentTheme.When(theme => {
            _ = ApplyTheme(theme, cancellationToken);
            return false;
        }, cancellationToken);

    private async Task ApplyTheme(Theme theme, CancellationToken cancellationToken)
    {
        using var _ = await _applyLock.Lock(cancellationToken).ConfigureAwait(false);
        if (_appliedTheme == theme)
            return;
        await _js
            .InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.setTheme", cancellationToken, theme.ToString())
            .ConfigureAwait(false);
        _appliedTheme = theme;
    }
}
