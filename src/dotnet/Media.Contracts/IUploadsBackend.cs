using ActualLab.Rpc;

namespace ActualChat.Media;

/// <summary>
/// Backend service for managing file upload sessions.
/// </summary>
public interface IUploadsBackend : IComputeService, IBackendService
{
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Upload?> Get(UploadId uploadId, CancellationToken cancellationToken);
    Task<long> GetOffset(UploadId uploadId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task OnCreate(UploadsBackend_Create command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemove(UploadsBackend_Remove command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<long> OnAppend(UploadsBackend_Append command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<MediaRef> OnConvertToMediaRef(UploadsBackend_ConvertToMediaRef command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<MediaRef> OnProcessAndSaveContent(UploadsBackend_ProcessAndSaveContent command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create a new upload session.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UploadsBackend_Create(
    [property: DataMember, Key(0)] UploadId UploadId,
    [property: DataMember, Key(1)] UserId UserId,
    [property: DataMember, Key(2)] long? Length,
    [property: DataMember, Key(3)] string Tag,
    [property: DataMember, Key(4)] MetadataBag Metadata
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UploadId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UploadId ShardKey => UploadId;
}

/// <summary>
/// Command to remove an upload session.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UploadsBackend_Remove(
    [property: DataMember, Key(0)] UploadId Id
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UploadId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UploadId ShardKey => Id;
}

/// <summary>
/// Command to append a chunk to an upload session.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UploadsBackend_Append(
    [property: DataMember, Key(0)] UploadId UploadId,
    [property: DataMember, Key(1)] long Offset,
    [property: DataMember, Key(2)] byte[] Chunk
) : ICommand<long>, IBackendCommand, IHasShardKey<UploadId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UploadId ShardKey => UploadId;
}

/// <summary>
/// Command to finalize an upload and convert it to media content.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UploadsBackend_ProcessAndSaveContent(
    [property: DataMember, Key(0)] UploadId UploadId,
    [property: DataMember, Key(1)] MediaId MediaId
) : ICommand<MediaRef>, IBackendCommand, IHasShardKey<UploadId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UploadId ShardKey => UploadId;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UploadsBackend_ConvertToMediaRef(
    [property: DataMember, Key(0)] UploadId UploadId
) : ICommand<MediaRef>, IBackendCommand, IHasShardKey<UploadId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UploadId ShardKey => UploadId;
}
