using RestEase;

namespace ActualChat.Chat.Frontend.Client;

/// <summary> Should be the same as <see cref="IChatServiceFrontend"/>. </summary>
[BasePath("chat")]
public interface IChatServiceFrontendDef
{
    [Get(nameof(TryGet))]
    Task<Chat?> TryGet(Session session, ChatId chatId, CancellationToken cancellationToken);

    [Get(nameof(GetIdRange))]
    Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [Get(nameof(GetEntryCount))]
    Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        Range<long>? idRange,
        CancellationToken cancellationToken);

    [Get(nameof(GetEntries))]
    Task<ImmutableArray<ChatEntry>> GetEntries(
        Session session,
        ChatId chatId,
        Range<long> idRange,
        CancellationToken cancellationToken);

    [Get(nameof(GetPermissions))]
    Task<ChatPermissions> GetPermissions(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);

    [Post(nameof(CreateChat))]
    Task<Chat> CreateChat([Body] IChatServiceFrontend.CreateChatCommand command, CancellationToken cancellationToken);

    [Post(nameof(CreateEntry))]
    Task<ChatEntry> CreateEntry([Body] IChatServiceFrontend.CreateEntryCommand command, CancellationToken cancellationToken);
}
