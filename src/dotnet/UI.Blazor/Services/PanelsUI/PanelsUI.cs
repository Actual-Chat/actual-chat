using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages the visibility and state of left, middle, and right UI panels.
/// </summary>
public partial class PanelsUI : UIWorkerBase<UIHub>
{
    // Must match --side-nav-transition-duration in side-nav.css
    public static readonly TimeSpan PanelTransitionDuration = TimeSpan.FromMilliseconds(200);
    private string? _keepPanelsUrl;

    public IState<ScreenSize> ScreenSize { get; }
    public LeftPanel Left { get; }
    public MiddlePanel Middle => field ??= Services.GetRequiredService<MiddlePanel>();
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
        this.Start();
    }

    public void HidePanels()
    {
        if (IsWide())
            return;

        ChatSwitchTracer.Mark("PanelsUI.HidePanels");
        Left.SetIsVisible(false);
        Right.SetIsVisible(false);
    }

    public void KeepPanelsOn(LocalUrl url)
        // Suppresses the auto-hide below for one upcoming navigation to `url` - the place switch changes the URL,
        // but the user asked for that place's chat list. Publication: ChatUI calls this off the dispatcher.
        => Volatile.Write(ref _keepPanelsUrl, url.Value);

    public async ValueTask HandleHistoryTransition(HistoryTransition transition)
    {
        if (transition.LocationChangeKind != LocationChangeKind.NewUri || IsWide())
            return;

        var url = new LocalUrl(transition.Item.Url);
        if (Interlocked.Exchange(ref _keepPanelsUrl, null) is { } keepPanelsUrl) {
            if (keepPanelsUrl == url.Value)
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
            // The panels slide away from the content that replaces them, not from its skeleton
            await Middle.WhenContentSwapped().ConfigureAwait(false);
            HidePanels();
        }
    }

    public bool IsWide()
        => ScreenSize.Value.IsWide();
}
