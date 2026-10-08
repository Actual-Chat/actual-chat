namespace ActualChat.Chat;

// The batch enumeration APIs indexers and summarizers scan with. None of them is a
// [ComputeMethod]: they page through whole chats, so caching them would be pointless.
public partial interface IChatsBackend
{
    Task<Chat[]> List(
        Moment minCreatedAt,
        ChatId? lastChatId,
        int limit,
        CancellationToken cancellationToken);

    Task<Chat[]> ListChanged(ChangedChatsQuery query, CancellationToken cancellationToken);

    Task<ChatEntry[]> ListChangedEntries(ChangedEntriesQuery query, CancellationToken cancellationToken);

    Task<ChatEntry[]> ListNewEntries(
        ChatId chatId,
        long minLocalIdExclusive,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// Query parameters for listing changed chats by version range.
/// </summary>
[DataContract, MessagePackObject]
public partial record ChangedChatsQuery
{
    [DataMember, Key(2)] public required ChatId? LastId { get; init; }
    [DataMember, Key(3)] public required int Limit { get; init; }
    [DataMember, Key(0)] public long MinVersion { get; init; }
    [DataMember, Key(1)] public long MaxVersion { get; init; } = long.MaxValue;
    [DataMember, Key(4)] public bool ExcludePeerChats { get; init; }
    [DataMember, Key(5)] public bool ExcludePlaceRootChats { get; init; }
}

/// <summary>
/// Query parameters for listing changed chat entries by version range.
/// </summary>
[DataContract, MessagePackObject]
public partial record ChangedEntriesQuery
{
    [DataMember, Key(0)] public long MinVersion { get; init; }
    [DataMember, Key(1)] public long MaxVersion { get; init; } = long.MaxValue;
    [DataMember, Key(2)] public long LastLocalId { get; init; }
    [DataMember, Key(3)] public ChatId ChatId { get; init; } = null!;
    [DataMember, Key(4)] public int Limit { get; init; }
    [DataMember, Key(5)] public bool RequireAttachments { get; init; }
}
