namespace ActualChat.Chat;

/// <summary>
/// Owner-driven import of prepared history into a group chat or a Place, under per-member
/// session consent. An import session is an <see cref="MaintenanceMode.Import"/> maintenance,
/// so ordinary chat activity is blocked for everyone, owners included, while it runs.
/// </summary>
public interface IChatImports : IComputeService
{
    [ComputeMethod]
    Task<ChatImportSession?> Get(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> HasConsent(
        Session session, ChatId chatId, ChatImportId importId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ChatImportConsentSummary> GetConsentSummary(
        Session session, ChatId chatId, int offset, int limit, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatImportSession> OnStart(ChatImports_Start command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnEnd(ChatImports_End command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetConsent(ChatImports_SetConsent command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ApiArray<ChatImportEntryResult>> OnImportEntries(
        ChatImports_ImportEntries command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<UploadId> OnCreateUpload(ChatImports_CreateUpload command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<MediaRef> OnFinalizeUpload(ChatImports_FinalizeUpload command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatImports_Start : ApiCommand<ChatImportSession>
{
    [DataMember, Key(2)] public required ChatId ChatId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatImports_End : ApiCommand<Unit>
{
    [DataMember, Key(2)] public required ChatId ChatId { get; init; }
    [DataMember, Key(3)] public required ChatImportId ImportId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatImports_SetConsent : ApiCommand<Unit>
{
    [DataMember, Key(2)] public required ChatId ChatId { get; init; }
    [DataMember, Key(3)] public required ChatImportId ImportId { get; init; }
    [DataMember, Key(4)] public required bool HasConsent { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatImports_ImportEntries : ApiCommand<ApiArray<ChatImportEntryResult>>, INotDeduplicated
{
    [DataMember, Key(2)] public required ChatId ChatId { get; init; }
    [DataMember, Key(3)] public required ChatImportId ImportId { get; init; }
    [DataMember, Key(4)] public required ApiArray<ChatImportEntry> Entries { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatImports_CreateUpload : ApiCommand<UploadId>
{
    [DataMember, Key(2)] public required ChatId ChatId { get; init; }
    [DataMember, Key(3)] public required ChatImportId ImportId { get; init; }
    [DataMember, Key(4)] public required UserId UserId { get; init; }
    [DataMember, Key(5)] public required long Length { get; init; }
    [DataMember, Key(6)] public required string FileName { get; init; }
    [DataMember, Key(7)] public required string ContentType { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatImports_FinalizeUpload : ApiCommand<MediaRef>
{
    [DataMember, Key(2)] public required ChatId ChatId { get; init; }
    [DataMember, Key(3)] public required ChatImportId ImportId { get; init; }
    [DataMember, Key(4)] public required UploadId UploadId { get; init; }
}
