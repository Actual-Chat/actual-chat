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
            await foreach (var _ in ScreenSize.Computed.ChangesUntyped(cancellationToken).ConfigureAwait(true)) {
                var isWide = IsWide();
                if (lastIsWide != isWide) {
                    lastIsWide = isWide;
                    // Recalculate left panel visibility when screen size is changed.
                    Left.SetIsVisible(Left.IsVisible.Value);
                }
            }
        });

    private Task TrackRightPanelSearchMode(CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(async () => {
            await foreach (var c in Right.IsSearchMode.Computed.ChangesUntyped(cancellationToken).ConfigureAwait(true)) {
                var cIsSearchMode = (Computed<bool>)c;
                if (!cIsSearchMode.Value)
                    continue;

                // Open right panel when search mode is triggered on.
                Right.SetIsVisible(true);
            }
        });
}
