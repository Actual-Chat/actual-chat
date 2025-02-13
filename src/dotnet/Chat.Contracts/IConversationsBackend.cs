using ActualChat.Flows;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IConversationsBackend : IComputeService, IBackendService
{

    [ComputeMethod]
    Task<Conversation?> Get(ConversationId conversationId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<Conversation>> List(ChatId chatId, Range<long> idTileRange, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Conversation> OnUpsert(ConversationBackend_Upsert command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Conversation> OnSummarize(ConversationBackend_Summarize command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Conversation> OnAppendReply(ConversationBackend_AppendReply command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] ConversationDiff Diff
) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_Summarize(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<ChatEntry> Entries
    ): ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>, IDelayed
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;

    [DataMember, MemoryPackOrder(2)]
    public Moment? DelayUntil { get; init; }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ConversationBackend_AppendReply(
    [property: DataMember, MemoryPackOrder(0)] ConversationId ConversationId,
    [property: DataMember, MemoryPackOrder(1)] long EntryLid,
    [property: DataMember, MemoryPackOrder(2)] ApiArray<ChatEntry> ReplySequence
) : ICommand<Conversation>, IBackendCommand, IHasShardKey<ChatId>, IDelayed
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ConversationId.ChatId;

    [DataMember, MemoryPackOrder(3)]
    public Moment? DelayUntil { get; init; }
}
