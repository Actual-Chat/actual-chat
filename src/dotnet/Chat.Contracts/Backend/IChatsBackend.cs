using ActualChat.Users;

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
    Task<AuthorRules> GetRules(
        string chatId,
        string principalId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<long> GetEntryCount(
        string chatId,
        ChatEntryKind entryKind,
        Range<long>? idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatTile> GetTile(
        string chatId,
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        bool includeRemoved,
        CancellationToken cancellationToken);

    // Note that it returns (firstId, lastId + 1) range!
    [ComputeMethod]
    Task<Range<long>> GetIdRange(
        string chatId,
        ChatEntryKind entryKind,
        bool includeRemoved,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Chat> Change(ChangeCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ChatEntry> UpsertEntry(UpsertEntryCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<TextEntryAttachment> CreateAttachment(CreateAttachmentCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] ChatId ChatId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<ChatDiff> Change,
        [property: DataMember] UserId CreatorUserId = default
    ) : ICommand<Chat>, IBackendCommand;

    [DataContract]
    public sealed record UpsertEntryCommand(
        [property: DataMember] ChatEntry Entry,
        [property: DataMember] bool HasAttachments = false
    ) : ICommand<ChatEntry>, IBackendCommand;

    [DataContract]
    public sealed record CreateAttachmentCommand(
        [property: DataMember]
        TextEntryAttachment Attachment
    ) : ICommand<TextEntryAttachment>, IBackendCommand;
}
