namespace ActualChat.Chat;

public partial interface IChatsBackend
{
    [ComputeMethod]
    Task<ChatImportSession?> GetImport(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<UserId>> ListImportConsents(
        ChatId chatId, ChatImportId importId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ChatImportUpload?> GetImportUpload(ChatId chatId, UploadId uploadId, CancellationToken cancellationToken);
    // Whether an import session with this ID has left any consents or batches behind
    Task<bool> HasImportRecords(ChatId chatId, ChatImportId importId, CancellationToken cancellationToken);
    Task<ChatEntryId?> FindEntryPostedSince(ChatId chatId, Moment since, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSetImportConsent(ChatsBackend_SetImportConsent command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ApiArray<ChatImportEntryResult>> OnImportEntries(
        ChatsBackend_ImportEntries command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRegisterImportUpload(ChatsBackend_RegisterImportUpload command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_SetImportConsent(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] UserId UserId,
    [property: DataMember, Key(2)] ChatImportId ImportId,
    [property: DataMember, Key(3)] bool HasConsent
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_ImportEntries(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] UserId UserId,
    [property: DataMember, Key(2)] ChatImportId ImportId,
    [property: DataMember, Key(3)] string BatchId,
    [property: DataMember, Key(4)] ApiArray<ChatImportEntry> Entries
) : ICommand<ApiArray<ChatImportEntryResult>>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

[DataContract, MessagePackObject]
public sealed partial record ChatImportUpload(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] ChatImportId ImportId,
    [property: DataMember, Key(2)] UploadId UploadId,
    [property: DataMember, Key(3)] UserId UserId,
    [property: DataMember, Key(4)] UserId UploadedBy,
    [property: DataMember, Key(5)] MediaRef? Media);

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsBackend_RegisterImportUpload(
    [property: DataMember, Key(0)] ChatImportUpload Upload
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Upload.ChatId.ShardKey;
}
