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

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Create(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] long? Length,
    [property: DataMember, Key(2)] string Tag,
    [property: DataMember, Key(3)] MetadataBag Metadata
) : ISessionCommand<UploadId>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Append(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] UploadId UploadId,
    [property: DataMember, Key(2)] long Offset,
    [property: DataMember, Key(3)] byte[] Chunk
) : ISessionCommand<long>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Remove(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] UploadId UploadId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_ConvertToMediaRef(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] UploadId UploadId
) : ISessionCommand<MediaRef>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_StartProcessUpload(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] UploadId UploadId,
    [property: DataMember, Key(2)] MediaId MediaId
) : ISessionCommand<Unit>, IApiCommand;
