using ActualChat.Attributes;
using ActualChat.Sharding;
using ActualChat.Time;
using ActualLab.Resilience;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IConversationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Conversation?> Get(ConversationId conversationId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Conversation[]> GetTile(
        ChatId chatId,
        Range<long> idTileRange,
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

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] ConversationId ConversationId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ConversationDiff> Change
) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ConversationId.ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_Summarize(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] Range<long>[] EntryIdRanges
    ) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>, IHasDelayUntil, IHasTimeout
{
    [DataMember, MemoryPackOrder(2)]
    public Moment DelayUntil { get; init; }

    ChatId IHasShardKey<ChatId>.ShardKey => ChatId;
    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(5);

    public override string ToString()
        => $"ConversationBackend_Summarize {{ ChatId={ChatId}, EntryIdRanges=[{string.Join(", ", EntryIdRanges.Select(r => r.Format()))}], DelayUntil={DelayUntil} }}";
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_AppendReply(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long EntryLid,
    [property: DataMember, MemoryPackOrder(2)] Range<long> ReplySequence
) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>, IHasDelayUntil, IHasTimeout
{
    [DataMember, MemoryPackOrder(3)]
    public Moment DelayUntil { get; init; }

    ChatId IHasShardKey<ChatId>.ShardKey => ChatId;
    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(5);
}
