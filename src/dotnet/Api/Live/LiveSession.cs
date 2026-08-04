using ActualChat.Chat;

namespace ActualChat.Live;

/// <summary>
/// A live call/session in a chat: its host, capability rules, members and per-peer state.
/// The transcript-summary block, when transcription is on, is the <see cref="Conversation"/> facet.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record LiveSession
{
    [DataMember(Order = 0), Key(0)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 1), Key(1)]
    public AuthorId Host { get; init; } = null!;
    [DataMember(Order = 2), Key(2)]
    public Moment StartedAt { get; init; }
    [DataMember(Order = 3), Key(3)]
    public SessionRules Rules { get; init; } = SessionRules.Default;
    [DataMember(Order = 4), Key(4)]
    public IReadOnlyList<LiveSessionMember> Members { get; init; } = [];
    [DataMember(Order = 5), Key(5)]
    public Conversation? Conversation { get; init; }
    [DataMember(Order = 6), Key(6)]
    public long Version { get; init; }
    [DataMember(Order = 7), Key(7)]
    public bool TranscriptionOn { get; init; }
    [DataMember(Order = 8), Key(8)]
    public LiveSessionKind Kind { get; init; } = LiveSessionKind.Ambient;
    [DataMember(Order = 9), Key(9)]
    public IReadOnlyList<CallInvite> Invites { get; init; } = [];
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ConversationId? ConversationId => Conversation?.Id;
}
