using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Chat;

public interface IChatsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Chat?> Get(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Chat?> GetTemplatedChatFor(ChatId templateId, UserId userId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatNews> GetNews(
        ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<AuthorRules> GetRules(
        ChatId chatId,
        PrincipalId principalId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<long> GetEntryCount(
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long>? idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatTile> GetTile(
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken);

    // Note that it returns (firstId, lastId + 1) range!
    [ComputeMethod]
    Task<Range<long>> GetIdRange(
        ChatId chatId,
        ChatEntryKind entryKind,
        bool includeRemoved,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<long?> GetMaxEntryVersion(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<ChatId>> GetPublicChatIdsFor(
        PlaceId placeId,
        CancellationToken cancellationToken);

    // Non-compute methods
    Task<ApiArray<Chat>> List(
        Moment minCreatedAt,
        ChatId lastChatId,
        int limit,
        CancellationToken cancellationToken);

    Task<ApiArray<Chat>> ListChanged(
        long minVersion,
        long maxVersion,
        ChatId lastId,
        int limit,
        CancellationToken cancellationToken);

    Task<Chat?> GetLastChanged(CancellationToken cancellationToken);

    Task<ApiList<ChatEntry>> ListChangedEntries(
        ChatId chatId,
        int limit,
        long maxLocalIdExclusive,
        long minVersionExclusive,
        CancellationToken cancellationToken);

    Task<ChatEntry?> FindNext(
        ChatId chatId,
        long? startEntryId,
        string text,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<CopiedChat?> GetCopiedChat(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatId> GetForwardChatReplacement(ChatId sourceChatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> OnChange(ChatsBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler, Obsolete("2024.01: Replaced with OnChangeEntry.")]
    Task<ChatEntry> OnUpsertEntry(ChatsBackend_UpsertEntry command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatEntry> OnChangeEntry(ChatsBackend_ChangeEntry command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ApiArray<TextEntryAttachment>> OnCreateAttachments(ChatsBackend_CreateAttachments command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveOwnChats(ChatsBackend_RemoveOwnChats command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveOwnEntries(ChatsBackend_RemoveOwnEntries command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnCreateNotesChat(ChatsBackend_CreateNotesChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatBackend_CopyChatResult> OnCopyChat(ChatBackend_CopyChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<CopiedChat> OnChangeCopiedChat(ChatsBackend_ChangeCopiedChat command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_CreateAttachments(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<TextEntryAttachment> Attachments
) : ICommand<ApiArray<TextEntryAttachment>>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatDiff> Change,
    [property: DataMember, MemoryPackOrder(3)] UserId OwnerId = default
) : ICommand<Chat>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}

[Obsolete("2024.01: Replaced with ChatsBackend_ChangeEntry.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_UpsertEntry(
    [property: DataMember, MemoryPackOrder(0)] ChatEntry Entry,
    [property: DataMember, MemoryPackOrder(1)] bool HasAttachments = false
) : ICommand<ChatEntry>, IBackendCommand, IHasShardKey<ChatEntryId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryId ShardKey => Entry.Id;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_ChangeEntry(
    [property: DataMember, MemoryPackOrder(0)] ChatEntryId ChatEntryId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ChatEntryDiff> Change
) : ICommand<ChatEntry>, IBackendCommand, IHasShardKey<ChatEntryId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryId ShardKey => ChatEntryId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_RemoveOwnChats(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<ChatEntry>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_RemoveOwnEntries(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<ChatEntry>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_CreateNotesChat(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<ChatEntry>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatBackend_CopyChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(2)] string CorrelationId
) : ICommand<ChatBackend_CopyChatResult>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatBackend_CopyChatResult(
    [property: DataMember, MemoryPackOrder(0)] bool HasChanges,
    [property: DataMember, MemoryPackOrder(1)] bool HasErrors,
    [property: DataMember, MemoryPackOrder(2)] long LastEntryId
);

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_ChangeCopiedChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<CopiedChatDiff> Change
) : ICommand<CopiedChat>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}
