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
public sealed partial record ChatThreads_Start : ApiCommand<Chat>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ParentChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required string Title { get; init; }
    [DataMember(Order = 4), Key(4)] public required string Description { get; init; }
    [DataMember(Order = 5), Key(5)] public required ChatEntryId[] EntryIds { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatThreads_ToggleThreadFollowStatus : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ThreadChatId ThreadChatId { get; init; }
}

/// <summary>
/// Statistics for a chat thread.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ThreadStat(
    [property: DataMember, Key(0)] long MessageCount,
    [property: DataMember, Key(1)] AuthorId[] TopAuthorIds,
    [property: DataMember, Key(2)] int AuthorCount,
    [property: DataMember, Key(3)] ChatEntryAttachment[] Attachments);
