using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages the visibility and state of left, middle, and right UI panels.
/// </summary>
public partial class PanelsUI : UIWorkerBase<UIHub>
{
    private string? _keepPanelsUrl;

    public IState<ScreenSize> ScreenSize { get; }
    public LeftPanel Left { get; }
    public MiddlePanel Middle { get; }
    public RightPanel Right { get; }

    public PanelsUI(UIHub hub) : base(hub)
    {
        var browserInfo = hub.BrowserInfo;
        if (!browserInfo.WhenReady.IsCompleted && !hub.IsPrerendering)
            throw StandardError.Internal(
                $"{nameof(PanelsUI)} is resolved too early: {nameof(BrowserInfo)} is not ready yet.");

        ScreenSize = browserInfo.ScreenSize;
        Left = new LeftPanel(this);
        Right = new RightPanel(this);
        Middle = new MiddlePanel(this);
        this.Start();
    }

    // Suppresses the auto-hide below for one upcoming navigation to `url`. The place switch needs it:
    // it changes the URL so the selection follows the place, but the user asked for that place's chat
    // list, and hiding the list is the one thing that would undo what they just did.
    public void KeepPanelsOn(LocalUrl url)
        => _keepPanelsUrl = url.Value;

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
        if (_keepPanelsUrl is { } keepPanelsUrl) {
            _keepPanelsUrl = null;
            if (string.Equals(keepPanelsUrl, url.Value, StringComparison.Ordinal))
                return;
        }
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
