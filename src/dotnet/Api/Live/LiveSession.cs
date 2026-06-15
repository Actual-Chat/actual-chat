using ActualChat.Chat;

namespace ActualChat.Live;

/// <summary>
/// A live call/session in a chat: its host, capability rules, members and per-peer state.
/// The transcript-summary block, when transcription is on, is the <see cref="Conversation"/> facet.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record LiveSession
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public AuthorId Host { get; init; } = null!;
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public Moment StartedAt { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public SessionRules Rules { get; init; } = SessionRules.Default;
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public IReadOnlyList<LiveSessionMember> Members { get; init; } = [];
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public LiveConversation? Conversation { get; init; }
    [DataMember(Order = 6), MemoryPackOrder(6), Key(6)]
    public long Version { get; init; }

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool TranscriptionOn => Conversation?.TranscriptionOn ?? false;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ConversationId? ConversationId => Conversation?.ConversationId;
}
