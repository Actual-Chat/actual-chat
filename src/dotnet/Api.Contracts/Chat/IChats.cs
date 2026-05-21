using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Service for managing chats, entries, and related operations.
/// </summary>
public interface IChats : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<AuthorRules> GetRules(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<ChatNews?> GetNews(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    // Note that it returns (firstId, lastId + 1) range!
    // Client-side methods always skip entries with IsRemoved == true
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    [Obsolete("2026.03: Use GetIdRange without entryKind")]
    Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        int entryKind,
        CancellationToken cancellationToken);

    // Client-side methods always skips entries with IsRemoved flag
    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<ChatTile> GetTile(
        Session session,
        ChatId chatId,
        Range<long> lidTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    [Obsolete("2026.03: Use GetTile without entryKind")]
    Task<ChatTile> GetTile(
        Session session,
        ChatId chatId,
        int entryKind,
        Range<long> lidTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<ChatRangeMeta> GetChatRangeMeta(
        Session session,
        ChatId chatId,
        long idTileStart,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<ChatContentTile> GetChatContentTile(
        Session session,
        ChatId chatId,
        ChatContentKind kindMask,
        Range<long> entryLidTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<ChatContentItem[]> ListChatContent(
        Session session,
        ChatId chatId,
        ChatContentKind kindMask,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Author[]> ListMentionableAuthors(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatCopyState?> GetChatCopyState(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatId?> GetForwardChatReplacement(Session session, ChatId sourceChatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ReadPositionsStat> GetReadPositionsStat(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<bool> IsEntryReadByMentionedUser(Session session, ChatEntryId chatEntryId, MentionRef mentionId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> OnChange(Chats_Change command, CancellationToken cancellationToken);

    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<ChatEntry> OnUpsertEntry(Chats_UpsertEntry command, CancellationToken cancellationToken);

    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity), LegacyName("OnRemoveTextEntry")]
    Task OnRemoveEntry(Chats_RemoveEntry command, CancellationToken cancellationToken);

    [CommandHandler, LegacyName("OnRestoreTextEntry")]
    Task OnRestoreEntry(Chats_RestoreEntry command, CancellationToken cancellationToken);

    [CommandHandler, LegacyName("OnRemoveTextEntries")]
    Task OnRemoveEntries(Chats_RemoveEntries command, CancellationToken cancellationToken);

    [CommandHandler, LegacyName("OnRestoreTextEntries")]
    Task OnRestoreEntries(Chats_RestoreEntries command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Chat> OnGetOrCreateFromTemplate(Chats_GetOrCreateFromTemplate command, CancellationToken cancellationToken);

    [CommandHandler, LegacyName("OnForwardTextEntries")]
    Task<Unit> OnForwardEntries(Chats_ForwardEntries command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Chat_CopyChatResult> OnCopyChat(Chat_CopyChat command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnPublishCopiedChat(Chat_PublishCopiedChat command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_GetOrCreateFromTemplate(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId TemplateChatId
) : ISessionCommand<Chat>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RemoveEntry(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long LocalId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RestoreEntry(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long LocalId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RemoveEntries(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long[] LocalIds
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RestoreEntries(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long[] LocalIds
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_UpsertEntry(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long? LocalId
) : ISessionCommand<ChatEntry>, IApiCommand, ISanitized
{
    [DataMember, MemoryPackOrder(3), Key(3)] public string Text { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember, MemoryPackOrder(4), Key(4)] public Option<long?> RepliedEntryLid { get; init; }
    [DataMember, MemoryPackOrder(11), Key(5)] public ChatEntryAttachment[] Attachments { get; init; } = [];
    [DataMember, MemoryPackOrder(12), Key(6)] public bool HasUploadingAttachments { get; init; }
    [DataMember, MemoryPackOrder(13), Key(7)] public string ClientId { get; init; } = "";
    [DataMember, MemoryPackOrder(14), Key(8)] public ChatEntryForwarded? Forwarded { get; init; }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_Change(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId? ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3), Key(3)] Change<ChatDiff> Change
) : ISessionCommand<Chat>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_ForwardEntries(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] ChatEntryId[] ChatEntries,
    [property: DataMember, MemoryPackOrder(3), Key(3)] ChatId[] DestinationChatIds
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chat_CopyChat(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId SourceChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(3), Key(3)] string CorrelationId
) : ISessionCommand<Chat_CopyChatResult>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chat_CopyChatResult(
    [property: DataMember, MemoryPackOrder(0), Key(0)] bool HasChanges,
    [property: DataMember, MemoryPackOrder(1), Key(1)] bool HasErrors
);

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chat_PublishCopiedChat(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] PlaceChatId NewChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] ChatId SourceChatId
) : ISessionCommand<Unit>, IApiCommand;
