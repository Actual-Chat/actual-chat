namespace ActualChat.Chat;

/// <summary>
/// Service for managing chat threads (reply threads attached to messages).
/// </summary>
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

    Task<(string, string)> SuggestThreadTitle(Session session, ChatId parentChatId, ChatEntryId[] entryIds, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Chat> OnStart(ChatThreads_Start command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Unit> OnToggleThreadFollowStatus(ChatThreads_ToggleThreadFollowStatus command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatThreads_Start(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ParentChatId,
    [property: DataMember, Key(2)] string Title,
    [property: DataMember, Key(3)] string Description,
    [property: DataMember, Key(4)] ChatEntryId[] EntryIds
) : ISessionCommand<Chat>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatThreads_ToggleThreadFollowStatus(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ThreadChatId ThreadChatId
) : ISessionCommand<Unit>, IApiCommand;

/// <summary>
/// Statistics for a chat thread.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ThreadStat(
    [property: DataMember, Key(0)] long MessageCount,
    [property: DataMember, Key(1)] AuthorId[] TopAuthorIds,
    [property: DataMember, Key(2)] int AuthorCount,
    [property: DataMember, Key(3)] ChatEntryAttachment[] Attachments);
