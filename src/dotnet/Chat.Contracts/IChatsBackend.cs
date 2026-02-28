using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for chat operations including entries, tiles, and chat management.
/// </summary>
public interface IChatsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Chat?> Get(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Chat?> GetTemplatedChatFor(ChatId templateId, UserId userId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatNews?> GetNews(
        ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<AuthorRules> GetRules(
        ChatId chatId,
        PrincipalId principalId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatTile> GetTile(
        ChatId chatId,
        Range<long> idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatRangeMeta> GetChatRangeMeta(
        ChatId chatId,
        long idTileStart,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatEntryRangeMeta> GetEntryRangeMeta(
        ChatId chatId,
        long idTileStart,
        CancellationToken cancellationToken);

    // Note that it returns (firstId, lastId + 1) range!
    [ComputeMethod]
    Task<Range<long>> GetIdRange(
        ChatId chatId,
        bool includeRemoved,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<long?> GetMaxEntryVersion(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatId[]> GetPublicChatIdsFor(
        PlaceId? placeId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<PlaceChatId[]> ListPlaceChatIds(
        PlaceId placeId,
        CancellationToken cancellationToken);

    // Non-compute methods

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

    Task<ChatEntry[]> ListEntries(
        ChatId chatId,
        Moment from,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatCopyState?> GetChatCopyState(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatId?> GetForwardChatReplacement(ChatId sourceChatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ReadPositionsStatBackend?> GetReadPositionsStat(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<PlaceChatId?> GetPlaceChatIdByAlias(PlaceId placeId, AliasId aliasId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> OnChange(ChatsBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatEntry> OnChangeEntry(ChatsBackend_ChangeEntry command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatEntryAttachment[]> OnCreateAttachments(ChatsBackend_CreateAttachments command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAttachments(ChatsBackend_RemoveAttachments command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveOwnChats(ChatsBackend_RemoveOwnChats command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveOwnEntries(ChatsBackend_RemoveOwnEntries command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnCreateNotesChat(ChatsBackend_CreateNotesChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatBackend_CopyChatResult> OnCopyChat(ChatBackend_CopyChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatCopyState> OnChangeChatCopyState(ChatsBackend_ChangeChatCopyState command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpdateReadPositionsStat(ChatsBackend_UpdateReadPositionsStat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRetranscribeChatEntry(ChatsBackend_RetranscribeChatEntry command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnNewAccountEvent(NewAccountEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnAuthorChangedEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnPlaceRemoved(PlaceChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create attachments for a text entry.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_CreateAttachments(
    [property: DataMember, MemoryPackOrder(0)]
    ChatEntryAttachment[] Attachments
) : ICommand<ChatEntryAttachment[]>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => Attachments.Length > 0 ? Attachments[0].EntryId.ChatId : throw new ArgumentException("No attachments provided", nameof(Attachments));
}

/// <summary>
/// Command to create, update, or delete a chat.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_RemoveAttachments(
    [property: DataMember, MemoryPackOrder(0)]
    ChatEntryId EntryId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => EntryId.ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] ChatId? ChatId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatDiff> Change,
    [property: DataMember, MemoryPackOrder(3)] UserId? OwnerId = null
) : ICommand<Chat>, IBackendCommand, IHasShardKey<ChatId?>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId? ShardKey => ChatId;
}

/// <summary>
/// Command to create, update, or delete a chat entry.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_ChangeEntry(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId ChatEntryId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatEntryDiff> Change
) : ICommand<ChatEntry>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ChatEntryId.ChatId;
}

/// <summary>
/// Command to remove all chats owned by a user.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_RemoveOwnChats(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<ChatEntry>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to remove all chat entries created by a user.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_RemoveOwnEntries(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<ChatEntry>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to create the user's personal notes chat.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_CreateNotesChat(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<ChatEntry>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to copy a chat to a different place.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatBackend_CopyChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(2)] string CorrelationId
) : ICommand<ChatBackend_CopyChatResult>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ChatId;
}

/// <summary>
/// Result of the chat copy operation.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatBackend_CopyChatResult(
    [property: DataMember, MemoryPackOrder(0)] bool HasChanges,
    [property: DataMember, MemoryPackOrder(1)] bool HasErrors,
    [property: DataMember, MemoryPackOrder(2)] long LastProcessedEntryId
);

/// <summary>
/// Command to update the chat copy state during migration.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_ChangeChatCopyState(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatCopyStateDiff> Change
) : ICommand<ChatCopyState>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ChatId;
}

/// <summary>
/// Command to update read positions statistics for a chat.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_UpdateReadPositionsStat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] UserId UserId,
    [property: DataMember, MemoryPackOrder(2)] long EntryLid
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => ChatId;
}

/// <summary>
/// Command to re-transcribe an audio entry in a different language.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_RetranscribeChatEntry(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId EntryId,
    [property: DataMember, MemoryPackOrder(1)] Language Language
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ShardKey => EntryId.ChatId;
}
