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
public sealed partial record Uploads_Create : ApiCommand<UploadId>
{
    [DataMember(Order = 2), Key(2)] public required long? Length { get; init; }
    [DataMember(Order = 3), Key(3)] public required string Tag { get; init; }
    [DataMember(Order = 4), Key(4)] public required MetadataBag Metadata { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Append : ApiCommand<long>
{
    [DataMember(Order = 2), Key(2)] public required UploadId UploadId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long Offset { get; init; }
    [DataMember(Order = 4), Key(4)] public required byte[] Chunk { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_Remove : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required UploadId UploadId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_ConvertToMediaRef : ApiCommand<MediaRef>
{
    [DataMember(Order = 2), Key(2)] public required UploadId UploadId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Uploads_StartProcessUpload : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required UploadId UploadId { get; init; }
    [DataMember(Order = 3), Key(3)] public required MediaId MediaId { get; init; }
}
