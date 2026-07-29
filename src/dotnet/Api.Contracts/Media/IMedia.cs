namespace ActualChat.Media;

public interface IMedia : IComputeService
{
    [ComputeMethod]
    Task<MediaProgress?> GetProgress(Session session, MediaId mediaId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<MediaRef?> GetContent(Session session, MediaId mediaId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<MediaId> OnReserveMedia(Media_ReserveMedia command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveMedia(Media_RemoveMedia command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpdateProgress(Media_UpdateProgress command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<MediaRef> OnProcessUpload(Media_ProcessUpload command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_ReserveMedia(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Scope
) : ISessionCommand<MediaId>, IApiCommand
{
    [DataMember, MemoryPackOrder(2), Key(2)] public MetadataBag Metadata { get; init; } = MetadataBag.Empty;
    [DataMember, MemoryPackOrder(3), Key(3)] public MediaKind Kind { get; init; }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_RemoveMedia(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] MediaId MediaId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_UpdateProgress(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3), Key(3)] MediaProcessingStage Stage,
    [property: DataMember, MemoryPackOrder(4), Key(4)] double StageProgress,
    [property: DataMember, MemoryPackOrder(5), Key(5)] string? Error = null
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_ProcessUpload(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] UploadId UploadId
) : ISessionCommand<MediaRef>, IApiCommand;
