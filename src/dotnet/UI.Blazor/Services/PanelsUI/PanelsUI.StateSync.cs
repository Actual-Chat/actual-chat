using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.Services;

partial class PanelsUI
{
    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(TrackScreenSize),
            AsyncChain.From(TrackRightPanelSearchMode),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .WithTransiencyResolver(TransiencyResolvers.PreferTransient)
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private Task TrackScreenSize(CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(async () => {
            var lastIsWide = IsWide();
            await foreach (var _ in ScreenSize.Changes(cancellationToken).ConfigureAwait(false)) {
                var isWide = IsWide();
                if (lastIsWide != isWide) {
                    lastIsWide = isWide;
                    // Recalculate left panel visibility when screen size is changed.
                    await Dispatcher
                        .InvokeSafeAsync(() => Left.SetIsVisible(Left.IsVisible.Value), Log)
                        .ConfigureAwait(false);
                }
            }
        });

    private Task TrackRightPanelSearchMode(CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(async () => {
            await foreach (var isSearchMode in Right.IsSearchMode.Changes(cancellationToken).ConfigureAwait(false)) {
                if (!isSearchMode.Value)
                    continue;

                // Open right panel when search mode is triggered on.
                await Dispatcher
                    .InvokeSafeAsync(() => Right.SetIsVisible(true), Log)
                    .ConfigureAwait(false);
            }
        });
}
