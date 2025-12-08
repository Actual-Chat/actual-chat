using ActualChat.Hashing;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Media;

public interface IUploads : IComputeService
{
    // Non computed method
    Task<long> GetOffset(Session session, UploadId uploadId, CancellationToken cancellationToken);
    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<UploadId> OnCreate(Uploads_Create command, CancellationToken cancellationToken);
    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task OnRemove(Uploads_Remove command, CancellationToken cancellationToken);
    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<long> OnAppend(Uploads_Append command, CancellationToken cancellationToken);
    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<MediaContent> OnConvertToMediaContent(Uploads_ConvertToMediaContent command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Create(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] long? Length,
    [property: DataMember, MemoryPackOrder(2)] string Tag,
    [property: DataMember, MemoryPackOrder(10)] PropertyBag Metadata
) : ISessionCommand<UploadId>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Append(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] UploadId UploadId,
    [property: DataMember, MemoryPackOrder(2)] long Offset,
    [property: DataMember, MemoryPackOrder(3)] byte[] Chunk
) : ISessionCommand<long>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Remove(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] UploadId UploadId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_ConvertToMediaContent(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] UploadId UploadId
) : ISessionCommand<MediaContent>, IApiCommand;
