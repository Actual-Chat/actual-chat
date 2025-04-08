using MemoryPack;

namespace ActualChat.Chat;

public interface IChatThreads : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<ChatId>> ListIds(Session session, ChatId parentChatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatThread> OnStart(ChatThreads_Start command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatThreads_Start(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ParentChatId,
    [property: DataMember, MemoryPackOrder(2)] string Title,
    [property: DataMember, MemoryPackOrder(3)] string Description,
    [property: DataMember, MemoryPackOrder(4)] ApiArray<TextEntryId> Entries
) : ISessionCommand<ChatThread>, IApiCommand;
