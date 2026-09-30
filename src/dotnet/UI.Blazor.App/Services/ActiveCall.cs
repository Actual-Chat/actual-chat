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
    CallId? CallId = null)
{
    // An unknown id matches any call in the chat: it is what the slot holds until the server answers.
    public bool IsCall(ChatId chatId, CallId? callId)
        => ChatId == chatId && (CallId is null || callId is null || CallId == callId);

    public bool IsSameCall([NotNullWhen(true)] ActiveCall? other)
        => other is not null && IsCall(other.ChatId, other.CallId);
}

public enum CallViewKind { None, Modal, FullScreen, Collapsed }

// Call stays set when Kind is None: that's how the view loop tells the slot got released.
public sealed record CallView(ActiveCall? Call, CallViewKind Kind, bool IsOverLock)
{
    public static readonly CallView None = new(null, CallViewKind.None, false);
}

internal readonly record struct CallScreenFlags(ChatId? CollapsedChatId, ChatId? OverLockChatId);
