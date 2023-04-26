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

    public bool IsNarrow()
        => ScreenSize.Value.IsNarrow();
    public bool IsWide()
        => ScreenSize.Value.IsWide();

    protected override Task OnRun(CancellationToken cancellationToken)
        => History.Dispatcher.InvokeAsync(async () => {
            var lastIsWide = IsWide();
            await foreach (var _ in ScreenSize.Changes(cancellationToken)) {
                var isWide = IsWide();
                if (lastIsWide != isWide) {
                    lastIsWide = isWide;
                    Left.SetIsVisible(Left.IsVisible.Value); // It changes to the right one anyway
                }
            }
        });
}
