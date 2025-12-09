using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Media;

public interface IUploadsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Upload?> Get(UploadId uploadId, CancellationToken cancellationToken);
    Task<Result<long>> GetOffset(UploadId uploadId, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnCreate(UploadsBackend_Create command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemove(UploadsBackend_Remove command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Result<long>> OnAppend(UploadsBackend_Append command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Result<MediaContent>> OnConvertToMediaContent(UploadsBackend_ConvertToMediaContent command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UploadsBackend_Create(
    [property: DataMember, MemoryPackOrder(0)] UploadId UploadId,
    [property: DataMember, MemoryPackOrder(1)] UserId UserId,
    [property: DataMember, MemoryPackOrder(2)] long? Length,
    [property: DataMember, MemoryPackOrder(3)] string Tag,
    [property: DataMember, MemoryPackOrder(10)] PropertyBag Metadata
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UploadId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UploadId ShardKey => UploadId;
}

// How should I apply sharding?

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UploadsBackend_Remove(
    [property: DataMember, MemoryPackOrder(0)] UploadId Id
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UploadId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UploadId ShardKey => Id;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UploadsBackend_Append(
    [property: DataMember, MemoryPackOrder(0)] UploadId UploadId,
    [property: DataMember, MemoryPackOrder(1)] long Offset,
    [property: DataMember, MemoryPackOrder(2)] byte[] Chunk
) : ICommand<Result<long>>, IBackendCommand, IHasShardKey<UploadId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UploadId ShardKey => UploadId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UploadsBackend_ConvertToMediaContent(
    [property: DataMember, MemoryPackOrder(0)] UploadId UploadId
) : ICommand<Result<MediaContent>>, IBackendCommand, IHasShardKey<UploadId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UploadId ShardKey => UploadId;
}
