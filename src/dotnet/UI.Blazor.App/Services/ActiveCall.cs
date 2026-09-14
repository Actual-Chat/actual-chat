namespace ActualChat.UI.Blazor.App.Services;

public sealed record IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo);

public enum CallOrigin { Incoming, Outgoing }

public enum CallPhase { Ringing, Dialing, Active }

// PeerId is the caller of an incoming call; an outgoing call holds the slot before anyone answers.
public sealed record ActiveCall(ChatId ChatId, CallOrigin Origin, CallPhase Phase, AuthorId? PeerId, bool HasVideo);

// What the chat's live session says about a call: none at all, still unanswered, or answered.
public enum CallSessionState { None, Dialing, Connected }

public readonly record struct CallFacts(IncomingCall? Ring, CallSessionState Session, bool IsInConversation);
