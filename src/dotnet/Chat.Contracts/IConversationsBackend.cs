using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IConversationsBackend : IComputeService, IBackendService
{

    // Commands

    [CommandHandler]
    Task<Conversation> OnUpsert(ConversationBackend_Upsert command, CancellationToken cancellationToken);

    //[CommandHandler]

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
