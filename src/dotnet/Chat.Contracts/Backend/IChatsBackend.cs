namespace ActualChat.Chat;

public interface IChatsBackend
{
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Chat?> Get(string chatId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetEntryCount(
        string chatId, ChatEntryType entryType, Range<long>? idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<ChatTile> GetTile(
        string chatId, ChatEntryType entryType, Range<long> idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken);
    // Note that it returns (firstId, lastId + 1) range!
    [ComputeMethod(KeepAliveTime = 1)]
    Task<Range<long>> GetIdRange(
        string chatId, ChatEntryType entryType,
        CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetMinId(
        string chatId, ChatEntryType entryType,
        CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<long> GetMaxId(
        string chatId, ChatEntryType entryType,
        CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<ChatPermissions> GetPermissions(
        string chatId, string? authorId,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> CreateChat(CreateChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatEntry> UpsertEntry(UpsertEntryCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<(ChatEntry AudioEntry, ChatEntry TextEntry)> CreateAudioEntry(
        CreateAudioEntryCommand command, CancellationToken cancellationToken);

    public record CreateChatCommand(Chat Chat) : ICommand<Chat>, IBackendCommand;
    public record CreateAudioEntryCommand(ChatEntry AudioEntry) : ICommand<(ChatEntry AudioEntry, ChatEntry TextEntry)>, IBackendCommand;
    public record UpsertEntryCommand(ChatEntry Entry) : ICommand<ChatEntry>, IBackendCommand;

    // Misc. low-level helpers

    Task<long> DbNextEntryId(
        object dbContext,
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);
}
