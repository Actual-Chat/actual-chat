using MemoryPack;

namespace ActualChat.Chat;

public interface IChatThreads : IComputeService
{
    [ComputeMethod]
    Task<ThreadChatId[]> ListIdsForChat(Session session, ChatId parentChatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ThreadChatId[]> ListIdsForPlace(Session session, PlaceId? parentPlaceId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> GetThreadFollowStatus(Session session, ThreadChatId threadChatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ThreadStat> GetThreadStat(Session session, ThreadChatId threadChatId, CancellationToken cancellationToken);

    [ComputeMethod]
    // Returns author in parent chat who created the thread.
    Task<Author?> GetThreadCreator(Session session, ThreadChatId threadChatId, CancellationToken cancellationToken);

    Task<(string, string)> SuggestThreadTitle(Session session, ChatId parentChatId, TextEntryId[] entryIds, CancellationToken cancellationToken);

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
    [property: DataMember, MemoryPackOrder(4)] TextEntryId[] EntryIds
) : ISessionCommand<Chat>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatThreads_ToggleThreadFollowStatus(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ThreadChatId ThreadChatId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ThreadStat(
    [property: DataMember, MemoryPackOrder(0)] long MessageCount,
    [property: DataMember, MemoryPackOrder(1)] AuthorId[] TopAuthorIds,
    [property: DataMember, MemoryPackOrder(2)] int AuthorCount);
