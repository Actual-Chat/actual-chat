using MemoryPack;

namespace ActualChat.Chat;

public interface IChats : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<Chat?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60), ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<AuthorRules> GetRules(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60, InvalidationDelay = 0.8)]
    Task<ChatNews> GetNews(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60), ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long>? idTileRange,
        CancellationToken cancellationToken);

    // Note that it returns (firstId, lastId + 1) range!
    // Client-side methods always skips entries with IsRemoved flag
    [ComputeMethod(MinCacheDuration = 60), ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        CancellationToken cancellationToken);

    // Client-side methods always skips entries with IsRemoved flag
    [ComputeMethod(MinCacheDuration = 10), ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<ChatTile> GetTile(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod, ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<ApiArray<Author>> ListMentionableAuthors(Session session, ChatId chatId, CancellationToken cancellationToken);

    // Non-compute methods

    Task<ChatEntry?> FindNext(
        Session session,
        ChatId chatId,
        long? startEntryId,
        string text,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> OnChange(Chats_Change command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatEntry> OnUpsertTextEntry(Chats_UpsertTextEntry command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRemoveTextEntry(Chats_RemoveTextEntry command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Chat> OnGetOrCreateFromTemplate(Chats_GetOrCreateFromTemplate command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Unit> OnForwardTextEntries(Chats_ForwardTextEntries command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_GetOrCreateFromTemplate(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId TemplateChatId
) : ISessionCommand<Chat>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RemoveTextEntry(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] long LocalId
) : ISessionCommand<Unit>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_UpsertTextEntry(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] long? LocalId,
    [property: DataMember, MemoryPackOrder(3)] string Text,
    [property: DataMember, MemoryPackOrder(4)] Option<long?> RepliedChatEntryId = default
) : ISessionCommand<ChatEntry>
{
    [DataMember, MemoryPackOrder(5)] public ApiArray<MediaId> Attachments { get; set; } = ApiArray<MediaId>.Empty;
    [DataMember, MemoryPackOrder(6)] public ChatEntryId ForwardedChatEntryId { get; set; }
    [DataMember, MemoryPackOrder(7)] public AuthorId ForwardedAuthorId { get; set; }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<ChatDiff> Change
) : ISessionCommand<Chat>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_ForwardTextEntries(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] ApiArray<ChatEntryId> ChatEntries,
    [property: DataMember, MemoryPackOrder(3)] ApiArray<ChatId> DestinationChatIds
) : ISessionCommand<Unit>;
