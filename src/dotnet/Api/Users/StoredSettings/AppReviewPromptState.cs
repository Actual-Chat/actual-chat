using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// The review prompt's history for a user, kept server-side so every device sees the same one.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record AppReviewPromptState
    : StoredSettings, IHasOrigin, IHasKvasKey<AppReviewPromptState>
{
    [DataMember, Key(0)] public string Origin { get; init; } = "";
    [DataMember, Key(1)] public Moment? LastPromptedAt { get; init; }
    [DataMember, Key(2)] public int PromptCount { get; init; }
    [DataMember, Key(3)] public int DeclineCount { get; init; }
    [DataMember, Key(4)] public ReviewPromptOutcome Outcome { get; init; }
    // Set when a live session ended with the user eligible; cleared by the next recorded outcome
    [DataMember, Key(5)] public Moment? PendingSince { get; init; }
    [DataMember, Key(6)] public ChatId? PendingChatId { get; init; }
}
