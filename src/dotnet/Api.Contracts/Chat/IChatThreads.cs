using MemoryPack;

namespace ActualChat.Chat;

public interface IChatThreads : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<ChatId>> ListIds(Session session, ChatId parentChatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> GetThreadFollowStatus(Session session, ChatId threadChatId, CancellationToken cancellationToken);

    Task<(string, string)> SuggestThreadTitle(Session session, ChatId parentChatId, ApiArray<TextEntryId> entryIds, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Chat> OnStart(ChatThreads_Start command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Unit> OnToggleThreadFollowStatus(ChatThreads_ToggleThreadFollowStatus command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatThreads_Start(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ParentChatId,
    [property: DataMember, MemoryPackOrder(2)] string Title,
    [property: DataMember, MemoryPackOrder(3)] string Description,
    [property: DataMember, MemoryPackOrder(4)] ApiArray<TextEntryId> EntryIds
) : ISessionCommand<Chat>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatThreads_ToggleThreadFollowStatus(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ThreadChatId
) : ISessionCommand<Unit>, IApiCommand;
