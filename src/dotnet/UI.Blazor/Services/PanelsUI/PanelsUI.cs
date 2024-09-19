using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public partial class PanelsUI : ScopedWorkerBase<UIHub>
{
    private History History => Hub.History;
    private Dispatcher Dispatcher => Hub.Dispatcher;

    public IState<ScreenSize> ScreenSize { get; }
    public LeftPanel Left { get; }
    public MiddlePanel Middle { get; }
    public RightPanel Right { get; }

    public PanelsUI(UIHub hub) : base(hub)
    {
        var browserInfo = hub.BrowserInfo;
        if (!browserInfo.WhenReady.IsCompleted && !hub.RenderModeSelector.IsPrerendering)
            throw StandardError.Internal(
                $"{nameof(PanelsUI)} is resolved too early: {nameof(BrowserInfo)} is not ready yet.");

        ScreenSize = browserInfo.ScreenSize;
        Left = new LeftPanel(this);
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

        var url = new LocalUrl(transition.Item.Url);
        if (!url.IsChatRoot()) {
            if (url.IsChat(out var chatId, out long entryLid)) {
                var oldUrl = new LocalUrl(transition.BaseItem.Url);
                if (oldUrl.IsChat(out var oldChatId, out long oldEntryLid) && chatId == oldChatId) {
                    // Same chat
                    if (entryLid == 0 && oldEntryLid != 0)
                        return; // Special case: do nothing on #entryLid removal
                }
            }

            // We want to make sure HidePanels() creates an additional history step,
            // otherwise "Back" from chat will hide the panel AND select the prev. chat.
            await History.WhenNavigationCompleted().ConfigureAwait(false);
            HidePanels();
        }
    }

    public bool IsWide()
        => ScreenSize.Value.IsWide();
}
