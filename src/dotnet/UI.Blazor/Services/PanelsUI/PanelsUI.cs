using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public class PanelsUI : WorkerBase, IHasServices
{
    public IServiceProvider Services { get; }
    public History History { get; }
    public IState<ScreenSize> ScreenSize { get; }

    public LeftPanel Left { get; }
    public MiddlePanel Middle { get; }
    public RightPanel Right { get; }

    public PanelsUI(IServiceProvider services)
    {
        Services = services;
        History = services.GetRequiredService<History>();

        var browserInfo = services.GetRequiredService<BrowserInfo>();
        if (!browserInfo.WhenReady.IsCompleted)
            throw StandardError.Internal(
                $"{nameof(PanelsUI)} is resolved too early: {nameof(BrowserInfo)} is not ready yet.");

        var autoNavigationUI = services.GetRequiredService<AutoNavigationUI>();
        var initialLeftPanelIsVisible = autoNavigationUI.InitialLeftPanelIsVisible;

        ScreenSize = browserInfo.ScreenSize;
        Left = new LeftPanel(this, initialLeftPanelIsVisible);
        Right = new RightPanel(this);
        Middle = new MiddlePanel(this);
        this.Start();
    }

    public void HidePanels()
    {
        if (IsWide())
            return;

        Left.SetIsVisible(false);
        Right.SetIsVisible(false);
    }

    public async ValueTask HandleHistoryTransition(HistoryTransition transition)
    {
        if (transition.LocationChangeKind != LocationChangeKind.NewUri)
            return;

        var url = new LocalUrl(transition.Item.Uri);
        if (!(IsWide() || url.IsChatRoot())) {
            await History.WhenNavigationCompleted;
            HidePanels();
        }
    }

    public bool IsWide()
        => ScreenSize.Value.IsWide();

    // Protected & private methods

    protected override Task OnRun(CancellationToken cancellationToken)
        => History.Dispatcher.InvokeAsync(async () => {
            var dispatcher = History.Dispatcher;
            var lastIsWide = IsWide();
            await foreach (var _ in ScreenSize.Changes(cancellationToken)) {
                var isWide = IsWide();
                if (lastIsWide != isWide) {
                    lastIsWide = isWide;
                    await dispatcher
                        .InvokeAsync(() => Left.SetIsVisible(Left.IsVisible.Value)) // Changes it to the right one
                        .ConfigureAwait(false);
                }
            }
        });
}
