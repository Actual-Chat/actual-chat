namespace ActualChat.Chat
{
    public interface IServerSideChatService : IChatService
    {
        // Commands
        [CommandHandler]
        Task<ChatEntry> CreateEntry(ChatCommands.CreateEntry command, CancellationToken cancellationToken);

        [CommandHandler]
        Task<ChatEntry> UpdateEntry(ChatCommands.UpdateEntry command, CancellationToken cancellationToken);

        // Queries
        [ComputeMethod(KeepAliveTime = 1)]
        Task<Chat?> TryGet(ChatId chatId, CancellationToken cancellationToken);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<long> GetEntryCount(
            ChatId chatId,
            Range<long>? idRange,
            CancellationToken cancellationToken);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<ImmutableArray<ChatEntry>> GetPage(
            ChatId chatId,
            Range<long> idRange,
            CancellationToken cancellationToken);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<Range<long>> GetMinMaxId(ChatId chatId, CancellationToken cancellationToken);

        [ComputeMethod(KeepAliveTime = 1)]
        Task<ChatPermissions> GetPermissions(ChatId chatId, UserId userId, CancellationToken cancellationToken);
    }
}
