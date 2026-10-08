namespace ActualChat.Chat;

// Everything that takes content away: wiping a chat's history, requesting a chat's removal,
// purging entries, and draining a removed account's chats and messages.
public partial interface IChatsBackend
{
    [ComputeMethod(ConsolidationDelay = 0)]
    Task<bool> IsRemovalPending(ChatId chatId, CancellationToken cancellationToken);
    // Not a [ComputeMethod]: ownership changes don't invalidate it, so every call reads it afresh.
    // These are the chats OnRequestUserRemoval removes along with the account.
    Task<ApiArray<ChatId>> ListSoleOwnedChatIds(UserId userId, CancellationToken cancellationToken);
    // Not a [ComputeMethod] either: ChatPurgeFlow asks it once it purged a wiped range, to drop it
    Task<bool> HasUnpurgedEntries(ChatId chatId, Range<long> entryLidRange, CancellationToken cancellationToken);

    // Commands - requests: each records what has to go, and ChatPurgeFlow purges it later

    [CommandHandler]
    Task<Range<long>> OnRequestHistoryWipe(
        ChatsBackend_RequestHistoryWipe command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRequestRemoval(ChatsBackend_RequestRemoval command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRequestUserRemoval(ChatsBackend_RequestUserRemoval command, CancellationToken cancellationToken);

    // Commands - purges: ChatPurgeFlow's steps, not meant to be called by anything else

    [CommandHandler]
    Task<int> OnPurgeEntryBatch(ChatsBackend_PurgeEntryBatch command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<int> OnPurgeAuthorEntries(ChatsBackend_PurgeAuthorEntries command, CancellationToken cancellationToken);
    // Called by the two above; rechecks every entry it's given under the chat's row lock
    [CommandHandler]
    Task<int> OnPurgeEntries(ChatsBackend_PurgeEntries command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_RequestHistoryWipe(
    [property: DataMember, Key(0)] ChatId ChatId,
    // Clamped to the entries there are when the command runs, so long.MaxValue ends it at the last one
    [property: DataMember, Key(1)] Range<long> EntryLidRange
) : ICommand<Range<long>>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_RequestRemoval(
    [property: DataMember, Key(0)] ChatId ChatId
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

/// <summary>
/// Requests the removal of the chats a user owns alone, and hands every chat entry the user created
/// to the purge of each chat it is in, which purges them through <see cref="ChatsBackend_PurgeAuthorEntries"/>.
/// </summary>

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_RequestUserRemoval(
    [property: DataMember, Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}

/// <summary>
/// Purges a batch of the entries an author created in a chat and returns how many it took;
/// fewer than a full batch means none are left.
/// </summary>

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_PurgeEntryBatch(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long ClearUntilEntryLid = 0,
    [property: DataMember, Key(2)] Range<long> WipeEntryLidRange = default
) : ICommand<int>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_PurgeAuthorEntries(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] AuthorId AuthorId
) : ICommand<int>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_PurgeEntries(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long[] LocalIds,
    [property: DataMember, Key(2)] AuthorId? RemovedAuthorId = null,
    [property: DataMember, Key(3)] long MaxClearedEntryLid = 0,
    [property: DataMember, Key(4)] Range<long> WipeEntryLidRange = default
) : ICommand<int>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}
