namespace ActualChat.Chat;

public interface IChatsBackend : IComputeService
{
    [ComputeMethod]
    Task<Chat?> Get(string chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListOwnedChatIds(string userId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<long> GetEntryCount(
        string chatId,
        ChatEntryType entryType,
        Range<long>? idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatTile> GetTile(
        string chatId,
        ChatEntryType entryType,
        Range<long> idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken);

    // Note that it returns (firstId, lastId + 1) range!
    [ComputeMethod]
    Task<Range<long>> GetIdRange(
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Range<long>> GetLastIdTile0(
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Range<long>> GetLastIdTile1(
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<long> GetMinId(
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<long> GetMaxId(
        string chatId,
        ChatEntryType entryType,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatAuthorRules> GetRules(
        string chatId,
        string chatPrincipalId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<TextEntryAttachment>> GetTextEntryAttachments(
        string chatId,
        long entryId,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> CreateChat(CreateChatCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Unit> UpdateChat(UpdateChatCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatEntry> UpsertEntry(UpsertEntryCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<TextEntryAttachment> CreateTextEntryAttachment(
        CreateTextEntryAttachmentCommand command,
        CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateChatCommand(
        [property: DataMember]
        Chat Chat
    ) : ICommand<Chat>, IBackendCommand;

    [DataContract]
    public sealed record UpdateChatCommand(
        [property: DataMember]
        Chat Chat
    ) : ICommand<Unit>, IBackendCommand;

    [DataContract]
    public sealed record CreateAudioEntryCommand(
        [property: DataMember]
        ChatEntry AudioEntry
    ) : ICommand<(ChatEntry AudioEntry, ChatEntry TextEntry)>, IBackendCommand;

    [DataContract]
    public sealed record UpsertEntryCommand(
        [property: DataMember]
        ChatEntry Entry
    ) : ICommand<ChatEntry>, IBackendCommand;

    [DataContract]
    public sealed record CreateTextEntryAttachmentCommand(
        [property: DataMember]
        TextEntryAttachment Attachment
    ) : ICommand<TextEntryAttachment>, IBackendCommand;
}
