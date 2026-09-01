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

    // Non computed method: for scans that walk a chat rather than render it. A scan reads a tile
    // per 5 entries, and as a compute method each of those becomes a cached, invalidation-tracked
    // slot - here and on the wire. This is a plain RPC call instead; the server still serves it
    // from the GetTile cache.
    Task<ChatTile> GetTileNonComputed(
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

    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<ApiArray<ChatEntryId>> ListPinnedEntries(Session session, ChatId chatId, CancellationToken cancellationToken);

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

    [CommandHandler]
    Task OnSetPinned(Chats_SetPinned command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_GetOrCreateFromTemplate : ApiCommand<Chat>
{
    [DataMember(Order = 2), Key(2)] public required ChatId TemplateChatId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RemoveEntry : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long LocalId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RestoreEntry : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long LocalId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RemoveEntries : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long[] LocalIds { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RestoreEntries : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long[] LocalIds { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_UpsertEntry : ApiCommand<ChatEntry>, ISanitized
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long? LocalId { get; init; }
    [DataMember(Order = 4), Key(4)] public string Text {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember(Order = 5), Key(5)] public Option<long?> RepliedEntryLid { get; init; }
    [DataMember(Order = 6), Key(6)] public ChatEntryAttachment[] Attachments { get; init; } = [];
    [DataMember(Order = 7), Key(7)] public bool HasUploadingAttachments { get; init; }
    [DataMember(Order = 8), Key(8)] public string ClientId { get; init; } = "";
    [DataMember(Order = 9), Key(9)] public ChatEntryForwarded? Forwarded { get; init; }
    [DataMember(Order = 10), Key(10)] public SharedLocationId? LocationId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_Change : ApiCommand<Chat>
{
    [DataMember(Order = 2), Key(2)] public required ChatId? ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long? ExpectedVersion { get; init; }
    [DataMember(Order = 4), Key(4)] public required Change<ChatDiff> Change { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_ForwardEntries : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required ChatEntryId[] ChatEntries { get; init; }
    [DataMember(Order = 4), Key(4)] public required ChatId[] DestinationChatIds { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_ForwardAttachment : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatEntryId ChatEntryId { get; init; }
    [DataMember(Order = 3), Key(3)] public required int AttachmentIndex { get; init; }
    [DataMember(Order = 4), Key(4)] public required ChatId[] DestinationChatIds { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chat_CopyChat : ApiCommand<Chat_CopyChatResult>
{
    [DataMember(Order = 2), Key(2)] public required ChatId SourceChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required PlaceId PlaceId { get; init; }
    [DataMember(Order = 4), Key(4)] public required string CorrelationId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chat_CopyChatResult(
    [property: DataMember, Key(0)] bool HasChanges,
    [property: DataMember, Key(1)] bool HasErrors
);

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_SetPinned : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatEntryId EntryId { get; init; }
    [DataMember(Order = 3), Key(3)] public required bool MustPin { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chat_PublishCopiedChat : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required PlaceChatId NewChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required ChatId SourceChatId { get; init; }
}
