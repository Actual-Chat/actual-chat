using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Media;

public interface IUploadsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Upload?> Get(UploadId uploadId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<UploadId> OnCreate(UploadsBackend_Create command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemove(UploadsBackend_Remove command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UploadsBackend_Create(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] long? Length,
    [property: DataMember, MemoryPackOrder(2)] string Tag,
    [property: DataMember, MemoryPackOrder(10)] PropertyBag Metadata
) : ICommand<UploadId>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
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


