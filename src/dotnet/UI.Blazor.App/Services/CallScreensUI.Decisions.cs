using ActualChat.Live;
namespace ActualChat.UI.Blazor.App.Services;

public partial class CallScreensUI
{
    internal static CallView DecideView(ActiveCall? call, bool isNarrow, CallScreenFlags flags)
    {
        if (call is null)
            return CallView.None;

        var chatId = call.ChatId;
        if (call.Role == CallRole.Callee && flags.OverLockChatId == chatId)
            return new CallView(call, CallViewKind.FullScreen, true);

        var isCollapsed = flags.CollapsedChatId == chatId;
        var kind = call.Phase switch {
            CallPhase.Ringing => isCollapsed ? CallViewKind.Collapsed : CallViewKind.Modal,
            // Dialing shows on the gesture, not on the server's answer: the invitee is known from the
            // click (or from the peer chat), and a refused call is taken off by the slot's release.
            CallPhase.Dialing when isCollapsed => CallViewKind.Collapsed,
            CallPhase.Dialing => isNarrow ? CallViewKind.FullScreen : CallViewKind.Modal,
            // A wide screen keeps an active call in its chat, so there's no full-screen view for the island
            // to lead back to.
            _ when !isNarrow => CallViewKind.None,
            _ => isCollapsed ? CallViewKind.Collapsed : CallViewKind.FullScreen,
        };
        return new CallView(call, kind, false);
    }

    internal static CallScreenState? DecideScreen(
        CallView view, ChatId? watchingChatId, VisualActivityPanelMode watchingMode)
    {
        // A call's own full-screen view comes first, with or without video. Otherwise the screen is
        // the watched chat's video, in whatever mode its panel is.
        if (view is { Kind: CallViewKind.FullScreen, Call: { } call })
            return new CallScreenState(
                call.ChatId, call, VisualActivityPanelMode.Expanded, watchingChatId == call.ChatId, view.IsOverLock);
        if (watchingChatId is not { } chatId)
            return null;

        var activeCall = view.Call is { Phase: CallPhase.Active } c && c.ChatId == chatId ? c : null;
        return new CallScreenState(chatId, activeCall, watchingMode, true, false);
    }

    internal static bool IsOverLockFlagStale(
        ChatId? overLockChatId, ChatId ringChatId, bool isSameRing, ChatId? heldChatId)
        => isSameRing && overLockChatId == ringChatId && heldChatId != ringChatId;
}
