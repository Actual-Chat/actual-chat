using ActualChat.UI.Blazor.Services;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The window the page runs in, for the hosts that have one to adjust: it follows the video panel,
/// so an expanded call filling the window isn't crowded by the window's own buttons.
/// Registered by the host that implements it - only the AppKit app so far.
/// </summary>
public abstract class WindowUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    protected override Task OnRun(CancellationToken cancellationToken)
    {
        // The two inputs are watched apart: the panel mode is a compute method and the screen size a
        // state, so a single computation over both can't be captured
        var chains = new[] {
            AsyncChain.From(WatchPanelMode),
            AsyncChain.From(WatchScreenSize),
        };
        return chains
            .Select(chain => chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(RetryDelaySeq.Exp(1, 60), Log)
                .CycleForever())
            .RunIsolated(cancellationToken);
    }

    // Protected methods

    protected abstract void SetInsetWindowButtons(bool mustInset);

    // Private methods

    private async Task WatchPanelMode(CancellationToken cancellationToken)
    {
        var cPanelMode = await Computed
            .Capture(() => Hub.ChatVideoUI.GetWatchingPanelMode(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        try {
            await foreach (var c in cPanelMode.Changes(cancellationToken).ConfigureAwait(false)) {
                if (!c.HasError)
                    Apply(c.Value, Hub.BrowserInfo.ScreenSize.Value);
            }
        }
        finally {
            // The buttons belong to the window, which outlives this scope
            SetInsetWindowButtons(false);
        }
    }

    private async Task WatchScreenSize(CancellationToken cancellationToken)
    {
        var cScreenSize = await Computed
            .Capture(() => Hub.BrowserInfo.ScreenSize.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cScreenSize.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var panelMode = await Hub.ChatVideoUI.GetWatchingPanelMode(cancellationToken).ConfigureAwait(false);
            Apply(panelMode, c.Value);
        }
    }

    private void Apply(VisualActivityPanelMode panelMode, ScreenSize screenSize)
        // Only the wide expanded panel runs its rounded corner under the buttons; the narrow one
        // starts below the titlebar
        => SetInsetWindowButtons(panelMode is VisualActivityPanelMode.Expanded && screenSize.IsWide());
}
