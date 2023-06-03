using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public class PanelsUI : WorkerBase, IHasServices
{
    public IServiceProvider Services { get; }
    public History History { get; }
    public Dispatcher Dispatcher { get; }
    public IState<ScreenSize> ScreenSize { get; }

    public LeftPanel Left { get; }
    public MiddlePanel Middle { get; }
    public RightPanel Right { get; }

    public PanelsUI(IServiceProvider services)
    {
        Services = services;
        History = services.GetRequiredService<History>();
        Dispatcher = History.Dispatcher;

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
        if (transition.LocationChangeKind != LocationChangeKind.NewUri || IsWide())
            return;

        var url = new LocalUrl(transition.Item.Uri);
        if (!url.IsChatRoot()) {
            if (url.IsChat(out var chatId, out long entryLid)) {
                var oldUrl = new LocalUrl(transition.BaseItem.Uri);
                if (oldUrl.IsChat(out var oldChatId, out long oldEntryLid) && chatId == oldChatId) {
                    // Same chat
                    if (entryLid == 0 && oldEntryLid != 0)
                        return; // Special case: do nothing on #entryLid removal
                }
            }

            // We want to make sure HidePanels() creates an additional history step,
            // otherwise "Back" from chat will hide the panel AND select the prev. chat.
            await History.WhenNavigationCompleted();
            HidePanels();
        }
    }

    public bool IsWide()
        => ScreenSize.Value.IsWide();

    // Protected & private methods

    protected override Task OnRun(CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(async () => {
            var lastIsWide = IsWide();
            await foreach (var _ in ScreenSize.Changes(cancellationToken)) {
                var isWide = IsWide();
                if (lastIsWide != isWide) {
                    lastIsWide = isWide;
                    await Dispatcher
                        .InvokeAsync(() => Left.SetIsVisible(Left.IsVisible.Value)) // Changes it to the right one
                        .ConfigureAwait(false);
                }
            }
        });
}
