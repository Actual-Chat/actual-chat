using ActualChat.UI.Blazor.Services;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Keeps the native titlebar in step with the video panel: an expanded call fills the wide window,
/// so the window buttons have to move clear of it. Does nothing where <see cref="INativeTitlebar"/>
/// isn't registered, which is everywhere but the AppKit app.
/// </summary>
public class NativeTitlebarUpdater(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var titlebar = Services.GetService<INativeTitlebar>();
        if (titlebar is null)
            return Task.CompletedTask;

        // The two inputs are watched apart: the panel mode is a compute method and the screen size a
        // state, so a single computation over both can't be captured
        var chains = new[] {
            AsyncChain.From(ct => WatchPanelMode(titlebar, ct)),
            AsyncChain.From(ct => WatchScreenSize(titlebar, ct)),
        };
        return chains
            .Select(chain => chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(RetryDelaySeq.Exp(1, 60), Log)
                .CycleForever())
            .RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task WatchPanelMode(INativeTitlebar titlebar, CancellationToken cancellationToken)
    {
        var cPanelMode = await Computed
            .Capture(() => Hub.ChatVideoUI.GetWatchingPanelMode(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        try {
            await foreach (var c in cPanelMode.Changes(cancellationToken).ConfigureAwait(false)) {
                if (!c.HasError)
                    Apply(titlebar, c.Value, Hub.BrowserInfo.ScreenSize.Value);
            }
        }
        finally {
            // The buttons belong to the window, which outlives this scope
            titlebar.SetInsetWindowButtons(false);
        }
    }

    private async Task WatchScreenSize(INativeTitlebar titlebar, CancellationToken cancellationToken)
    {
        var cScreenSize = await Computed
            .Capture(() => Hub.BrowserInfo.ScreenSize.Use(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var c in cScreenSize.Changes(cancellationToken).ConfigureAwait(false)) {
            if (c.HasError)
                continue;

            var panelMode = await Hub.ChatVideoUI.GetWatchingPanelMode(cancellationToken).ConfigureAwait(false);
            Apply(titlebar, panelMode, c.Value);
        }
    }

    private static void Apply(INativeTitlebar titlebar, VisualActivityPanelMode panelMode, ScreenSize screenSize)
        // Only the wide expanded panel runs its rounded corner under the buttons; the narrow one
        // starts below the titlebar
        => titlebar.SetInsetWindowButtons(panelMode is VisualActivityPanelMode.Expanded && screenSize.IsWide());
}
