using RestEase;

namespace ActualChat.Chat.Client
{
    [BasePath("chat")]
    public interface IChatClientDef
    {
        // Commands
        [Post(nameof(CreateChat))]
        Task<Chat> CreateChat([Body] ChatCommands.CreateChat command, CancellationToken cancellationToken);
        [Post(nameof(PostMessage))]
        Task<ChatEntry> PostMessage([Body] ChatCommands.PostMessage command, CancellationToken cancellationToken);

        // Queries
        [Get(nameof(TryGet))]
        Task<Chat?> TryGet(Session session, ChatId chatId, CancellationToken cancellationToken);

        [Get(nameof(GetMinMaxId))]
        Task<Range<long>> GetMinMaxId(
            Session session, ChatId chatId,
            CancellationToken cancellationToken);
        [Get(nameof(GetEntryCount))]
        Task<long> GetEntryCount(
            Session session, ChatId chatId, Range<long>? idRange,
            CancellationToken cancellationToken);

        [Get(nameof(GetEntries))]
        Task<ImmutableArray<ChatEntry>> GetEntries(
            Session session, ChatId chatId, Range<long> idRange,
            CancellationToken cancellationToken);

        [Get(nameof(GetPermissions))]
        Task<ChatPermissions> GetPermissions(
            Session session, ChatId chatId,
            CancellationToken cancellationToken);
    }
}
