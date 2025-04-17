using MemoryPack;

namespace ActualChat.Chat;

public interface IChatThreads : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<ChatId>> ListIdsForChat(Session session, ChatId parentChatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<ChatId>> ListIdsForPlace(Session session, PlaceId parentPlaceId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> GetThreadFollowStatus(Session session, ChatId threadChatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ThreadStat> GetThreadStat(Session session, ChatId threadChatId, CancellationToken cancellationToken);

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

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ThreadStat(
    [property: DataMember, MemoryPackOrder(0)] long MessageCount,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<AuthorId> TopAuthorIds,
    [property: DataMember, MemoryPackOrder(2)] int AuthorCount);
