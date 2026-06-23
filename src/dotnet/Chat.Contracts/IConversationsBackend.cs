using ActualChat.Time;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for managing conversations (chat entry groupings with AI summaries).
/// </summary>
public interface IConversationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Conversation?> Get(ConversationId conversationId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Conversation[]> GetTile(
        ChatId chatId,
        Range<long> lidTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ConversationRangeMeta> GetRangeMeta(ChatId chatId, long idTileStart, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Conversation> OnChange(ConversationBackend_Change command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Conversation> OnSummarize(ConversationBackend_Summarize command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Conversation?> OnAppendReply(ConversationBackend_AppendReply command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or delete a conversation.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_Change(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ConversationId ConversationId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Change<ConversationDiff> Change
) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>
{
    [DataMember, MemoryPackOrder(3), Key(3)]
    public bool IsLiveMaterialization { get; init; }

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ConversationId.ChatId;
}

/// <summary>
/// Command to generate an AI summary for a conversation.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_Summarize(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Range<long>[] EntryLidRanges
    ) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>, IHasDelayUntil, IHasTimeout
{
    [DataMember, MemoryPackOrder(2), Key(2)]
    public Moment DelayUntil { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)]
    public bool IsLiveMaterialization { get; init; }

    ChatId IHasShardKey<ChatId>.ShardKey => ChatId;
    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(5);

    public override string ToString()
        => $"ConversationBackend_Summarize {{ ChatId={ChatId}, EntryLidRanges=[{string.Join(", ", EntryLidRanges.Select(r => r.Format()))}], DelayUntil={DelayUntil} }}";
}

/// <summary>
/// Command to append a reply entry range to a conversation.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_AppendReply(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long EntryLid,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Range<long> ReplyLidRange
) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>, IHasDelayUntil, IHasTimeout
{
    [DataMember, MemoryPackOrder(3), Key(3)]
    public Moment DelayUntil { get; init; }

    ChatId IHasShardKey<ChatId>.ShardKey => ChatId;
    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(5);
}
