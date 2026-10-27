using ActualChat.Live;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record IncomingCall(CallId CallId, AuthorId Caller, bool HasVideo)
{
    public ChatId ChatId => CallId.ChatId;
}

// PeerId is the caller of an incoming call; an outgoing call holds the slot before anyone answers.
// CallId is null until the server names the call: a call placed or answered here holds the slot before that.
public sealed record ActiveCall(
    ChatId ChatId,
    CallRole Role,
    CallPhase Phase,
    AuthorId? PeerId,
    bool HasVideo,
    CallId? CallId = null,
    bool IsPeerRinging = false)
{
    // An unknown id matches any call in the chat: it is what the slot holds until the server answers.
    public bool IsCall(ChatId chatId, CallId? callId)
        => ChatId == chatId && (CallId is null || callId is null || CallId == callId);

    public bool IsSameCall([NotNullWhen(true)] ActiveCall? other)
        => other is not null && IsCall(other.ChatId, other.CallId);
}

// How far the call has got, as its screens tell it. Requesting: the server hasn't named the call yet;
// Calling: no callee's device confirmed it rings; Connecting: answered, but not both sides are on it.
public enum CallStage { None, Requesting, Calling, Ringing, Incoming, Connecting, Connected }

public enum CallViewKind { None, Modal, FullScreen, Collapsed }

// Call stays set when Kind is None: that's how the view loop tells the slot got released.
public sealed record CallView(ActiveCall? Call, CallViewKind Kind, bool IsOverLock)
{
    public static readonly CallView None = new(null, CallViewKind.None, false);
}

// What the call screen shows: a call's own screen, or the video of a chat nobody called in (Call is null).
// Mode is the mode of the whole screen; a call without video is only ever Expanded.
public sealed record CallScreenState(
    ChatId ChatId,
    ActiveCall? Call,
    VisualActivityPanelMode Mode,
    bool HasVideo,
    bool IsOverLock)
{
    public bool HasHangUp
        // Inline in the chat, the screen leaves hanging up to the chat header
        => Mode == VisualActivityPanelMode.Expanded
            || (Mode == VisualActivityPanelMode.Collapsed && Call is { Phase: CallPhase.Active });
}

internal readonly record struct CallScreenFlags(ChatId? CollapsedChatId, ChatId? OverLockChatId);
