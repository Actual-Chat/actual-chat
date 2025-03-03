using ActualChat.UI.Blazor;

namespace ActualChat.App.Maui.Services;

public class KeepWebViewAliveUI(UIHub hub) : WorkerBase
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    [field: AllowNull, MaybeNull]
    public IMutableState<bool> IsEnabled => field ??= hub.StateFactory().NewMutable<bool>();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor(GetType());

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(KeepWebViewAlive)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(3, 60))
            .AppendDelay(Interval)
            .CycleForever()
            .RunIsolated(cancellationToken);

    private async Task KeepWebViewAlive(CancellationToken cancellationToken)
    {
        await IsEnabled.When(x => x, cancellationToken).ConfigureAwait(false);
        if (MauiWebView.Current is { } view)
            await view.EvaluateJS("1 + 1").ConfigureAwait(false);
    }
}
