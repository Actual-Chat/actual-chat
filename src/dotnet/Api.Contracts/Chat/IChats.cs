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

    // Returns ChatNews with a slim LastTextEntry (see ChatNews.ToSlim); the LegacyName
    // aliases below route v2.12- clients calling wire name "GetNews" to GetFullNews.
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    [LegacyName("GetNews_NewUnused", "2.12.9999")]
    Task<ChatNews?> GetNews(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    [LegacyName(nameof(GetNews), "2.12.9999")]
    [Obsolete("2026.07: Use GetNews - it returns a slim LastTextEntry, which is all the UI needs.")]
    Task<ChatNews?> GetFullNews(
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

    // Client-side methods always skips entries with IsRemoved flag
    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<ChatTile> GetTile(
        Session session,
        ChatId chatId,
        Range<long> lidTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<ChatRangeMeta> GetChatRangeMeta(
        Session session,
        ChatId chatId,
        long idTileStart,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<ChatContentSkeleton> GetContentPeriods(
        Session session,
        ChatId chatId,
        ChatContentKind kind,
        string? beforePeriodKey,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<VisualMediaItem[]> GetVisualMediaPeriod(
        Session session,
        ChatId chatId,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<FileItem[]> GetFilePeriod(
        Session session,
        ChatId chatId,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<LinkItem[]> GetLinkPeriod(
        Session session,
        ChatId chatId,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Author[]> ListMentionableAuthors(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatCopyState?> GetChatCopyState(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatId?> GetForwardChatReplacement(Session session, ChatId sourceChatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ReadPositionsStat> GetReadPositionsStat(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Consolidated: this is monotone - it flips false -> true once - but every read-position advance
    // by the mentioned user invalidates it for every rendered entry that mentions them.
    [ComputeMethod(ConsolidationDelay = 0.2)]
    Task<bool> IsEntryReadByMentionedUser(
        Session session,
        ChatEntryId chatEntryId,
        MentionRef mentionId,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> OnChange(Chats_Change command, CancellationToken cancellationToken);

    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<ChatEntry> OnUpsertEntry(Chats_UpsertEntry command, CancellationToken cancellationToken);

    // Editing an entry older than Constants.Chat.MaxStreamingEditAge still succeeds, but lands as
    // one ordinary update rather than re-animating a settled message.
    [RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<ChatEntry> StreamEntry(
        Session session,
        ChatId chatId,
        long? localId,
        RpcStream<string> textChunks,
        CancellationToken cancellationToken);

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
    Task<Unit> OnForwardAttachment(Chats_ForwardAttachment command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Chat_CopyChatResult> OnCopyChat(Chat_CopyChat command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnPublishCopiedChat(Chat_PublishCopiedChat command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_GetOrCreateFromTemplate(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId TemplateChatId
) : ISessionCommand<Chat>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RemoveEntry(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] long LocalId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RestoreEntry(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] long LocalId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RemoveEntries(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] long[] LocalIds
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RestoreEntries(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] long[] LocalIds
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_UpsertEntry(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] long? LocalId
) : ISessionCommand<ChatEntry>, IApiCommand, ISanitized
{
    [DataMember, Key(3)] public string Text {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember, Key(4)] public Option<long?> RepliedEntryLid { get; init; }
    [DataMember, Key(5)] public ChatEntryAttachment[] Attachments { get; init; } = [];
    [DataMember, Key(6)] public bool HasUploadingAttachments { get; init; }
    [DataMember, Key(7)] public string ClientId { get; init; } = "";
    [DataMember, Key(8)] public ChatEntryForwarded? Forwarded { get; init; }
    [DataMember, Key(9)] public SharedLocationId? LocationId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_Change(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId? ChatId,
    [property: DataMember, Key(2)] long? ExpectedVersion,
    [property: DataMember, Key(3)] Change<ChatDiff> Change
) : ISessionCommand<Chat>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_ForwardEntries(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] ChatEntryId[] ChatEntries,
    [property: DataMember, Key(3)] ChatId[] DestinationChatIds
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_ForwardAttachment(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatEntryId ChatEntryId,
    [property: DataMember, Key(2)] int AttachmentIndex,
    [property: DataMember, Key(3)] ChatId[] DestinationChatIds
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chat_CopyChat(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId SourceChatId,
    [property: DataMember, Key(2)] PlaceId PlaceId,
    [property: DataMember, Key(3)] string CorrelationId
) : ISessionCommand<Chat_CopyChatResult>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chat_CopyChatResult(
    [property: DataMember, Key(0)] bool HasChanges,
    [property: DataMember, Key(1)] bool HasErrors
);

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chat_PublishCopiedChat(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] PlaceChatId NewChatId,
    [property: DataMember, Key(2)] ChatId SourceChatId
) : ISessionCommand<Unit>, IApiCommand;
