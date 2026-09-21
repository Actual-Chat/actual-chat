namespace ActualChat.Chat;

public partial interface IChatsBackend
{
    [ComputeMethod]
    Task<ChatImportSession?> GetImport(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<UserId>> ListImportConsents(ChatId chatId, string importId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ChatImportUpload?> GetImportUpload(ChatId chatId, UploadId uploadId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatImportSession> OnStartImport(ChatsBackend_StartImport command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnEndImport(ChatsBackend_EndImport command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetImportConsent(ChatsBackend_SetImportConsent command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ApiArray<ChatImportEntryResult>> OnImportEntries(
        ChatsBackend_ImportEntries command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRegisterImportUpload(ChatsBackend_RegisterImportUpload command, CancellationToken cancellationToken);
    [EventHandler]
    Task OnImportChangedEvent(ChatImportChangedEvent command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
public sealed partial record ChatsBackend_StartImport(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] UserId UserId,
    [property: DataMember, Key(2)] string ImportId
) : ICommand<ChatImportSession>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

[DataContract, MessagePackObject]
public sealed partial record ChatsBackend_EndImport(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] UserId UserId,
    [property: DataMember, Key(2)] string ImportId
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

[DataContract, MessagePackObject]
public sealed partial record ChatsBackend_SetImportConsent(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] UserId UserId,
    [property: DataMember, Key(2)] string ImportId,
    [property: DataMember, Key(3)] bool HasConsent
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}

[DataContract, MessagePackObject]
public sealed partial record ChatsBackend_ImportEntries(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] UserId UserId,
    [property: DataMember, Key(2)] string ImportId,
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
    [property: DataMember, Key(1)] string ImportId,
    [property: DataMember, Key(2)] UploadId UploadId,
    [property: DataMember, Key(3)] UserId UserId,
    [property: DataMember, Key(4)] UserId UploadedBy,
    [property: DataMember, Key(5)] MediaRef? Media);

[DataContract, MessagePackObject]
public sealed partial record ChatsBackend_RegisterImportUpload(
    [property: DataMember, Key(0)] ChatImportUpload Upload
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Upload.ChatId.ShardKey;
}

[DataContract, MessagePackObject(true)]
public sealed partial record ChatImportChangedEvent(
    [property: DataMember] ChatId ChatId,
    [property: DataMember] string ImportId
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
}
