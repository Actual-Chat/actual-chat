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
            _ when flags.InChatChatId == chatId => CallViewKind.None,
            // A wide screen keeps an active call in its chat.
            _ => isNarrow ? CallViewKind.FullScreen : CallViewKind.None,
        };
        return new CallView(call, kind, false);
    }

    internal static bool IsOverLockFlagStale(
        ChatId? overLockChatId, ChatId ringChatId, bool isSameRing, ChatId? heldChatId)
        => isSameRing && overLockChatId == ringChatId && heldChatId != ringChatId;
}
