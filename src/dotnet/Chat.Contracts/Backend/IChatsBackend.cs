namespace ActualChat.Chat;

public partial interface IChatsBackend : IComputeService
{
    [ComputeMethod]
    Task<Chat?> Get(string chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatSummary?> GetSummary(
        string chatId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatAuthorRules> GetRules(
        string chatId,
        string chatPrincipalId,
        CancellationToken cancellationToken);

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
        bool includeRemoved,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> Change(ChangeCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatEntry> UpsertEntry(UpsertEntryCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<TextEntryAttachment> CreateTextEntryAttachment(
        CreateTextEntryAttachmentCommand command,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task<Chat> CreateAnnouncementsChat(CreateAnnouncementsChatCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task UpgradeChat(UpgradeChatCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Symbol ChatId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<ChatDiff> Change,
        [property: DataMember] Symbol CreatorUserId = default
    ) : ICommand<Chat>, IBackendCommand;

    [DataContract]
    public sealed record CreateAudioEntryCommand(
        [property: DataMember]
        ChatEntry AudioEntry
    ) : ICommand<(ChatEntry AudioEntry, ChatEntry TextEntry)>, IBackendCommand;

    [DataContract]
    public sealed record UpsertEntryCommand(
        [property: DataMember] ChatEntry Entry,
        [property: DataMember] bool HasAttachments = false
    ) : ICommand<ChatEntry>, IBackendCommand;

    [DataContract]
    public sealed record CreateTextEntryAttachmentCommand(
        [property: DataMember]
        TextEntryAttachment Attachment
    ) : ICommand<TextEntryAttachment>, IBackendCommand;

    [DataContract]
    public sealed record UpgradeChatCommand(
        [property: DataMember] Symbol ChatId
    ) : ICommand<Unit>, IBackendCommand;

    [DataContract]
    public sealed record CreateAnnouncementsChatCommand(
    ) : ICommand<Chat>, IBackendCommand;
}
