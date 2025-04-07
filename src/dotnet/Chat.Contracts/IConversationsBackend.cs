using ActualChat.Attributes;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IConversationsBackend : IComputeService, IBackendService
{

    [ComputeMethod]
    Task<Conversation?> Get(ConversationId conversationId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ConversationId[]> List(ChatId chatId, Range<long> idTileRange, CancellationToken cancellationToken);

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

[Queue("SummarizeQueue")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_Summarize(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] Range<long>[] EntryIdRanges
    ): ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>, IDelayed
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;

    [DataMember, MemoryPackOrder(2)]
    public Moment? DelayUntil { get; init; }
}

[Queue("SummarizeQueue")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_AppendReply(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long EntryLid,
    [property: DataMember, MemoryPackOrder(2)] Range<long> ReplySequence
) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>, IDelayed
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;

    [DataMember, MemoryPackOrder(3)]
    public Moment? DelayUntil { get; init; }
}
