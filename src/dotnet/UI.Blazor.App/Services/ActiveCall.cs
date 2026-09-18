using ActualChat.Live;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo);

// PeerId is the caller of an incoming call; an outgoing call holds the slot before anyone answers.
public sealed record ActiveCall(ChatId ChatId, CallRole Role, CallPhase Phase, AuthorId? PeerId, bool HasVideo);

public enum CallViewKind { None, Modal, FullScreen, Collapsed }

// Call stays set when Kind is None: that's how the view loop tells the slot got released.
public sealed record CallView(ActiveCall? Call, CallViewKind Kind, bool IsOverLock)
{
    public static readonly CallView None = new(null, CallViewKind.None, false);
}

internal readonly record struct CallScreenFlags(
    ChatId? CollapsedChatId, ChatId? InChatChatId, ChatId? OverLockChatId);
