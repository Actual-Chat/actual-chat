using ActualLab.Rpc;

namespace ActualChat.Media;

/// <summary>
/// Service for managing chunked file uploads.
/// </summary>
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
    [RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<long> AppendStream(Session session, UploadId uploadId, long offset, RpcStream<byte[]> dataStream, CancellationToken cancellationToken);
    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<MediaRef> OnConvertToMediaContent(Uploads_ConvertToMediaRef command, CancellationToken cancellationToken);
    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task OnStartProcessUpload(Uploads_StartProcessUpload command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Create(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long? Length,
    [property: DataMember, MemoryPackOrder(2), Key(2)] string Tag,
    [property: DataMember, MemoryPackOrder(10), Key(3)] MetadataBag Metadata
) : ISessionCommand<UploadId>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Append(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] UploadId UploadId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long Offset,
    [property: DataMember, MemoryPackOrder(3), Key(3)] byte[] Chunk
) : ISessionCommand<long>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Remove(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] UploadId UploadId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_ConvertToMediaRef(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] UploadId UploadId
) : ISessionCommand<MediaRef>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_StartProcessUpload(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] UploadId UploadId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] MediaId MediaId
) : ISessionCommand<Unit>, IApiCommand;
