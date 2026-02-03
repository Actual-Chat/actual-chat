using MemoryPack;

namespace ActualChat.Media;

public interface IMedias : IComputeService
{
    [ComputeMethod]
    Task<MediaStatusInfo?> GetStatus(Session session, MediaId mediaId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<MediaId> OnReserveMedia(Medias_ReserveMedia command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveMedia(Medias_RemoveMedia command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpdateStatus(Medias_UpdateStatus command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<MediaContent> OnProcessUpload(Medias_ProcessUpload command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Medias_ReserveMedia(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string Scope
) : ISessionCommand<MediaId>, IApiCommand
{
    [DataMember, MemoryPackOrder(2)] public PropertyBag Metadata { get; init; } = PropertyBag.Empty;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Medias_RemoveMedia(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] MediaId MediaId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Medias_UpdateStatus(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(2)] MediaStatus Status,
    [property: DataMember, MemoryPackOrder(3)] MediaPreparingStage PreparingStage = MediaPreparingStage.None,
    [property: DataMember, MemoryPackOrder(4)] double StageProgress = 0
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Medias_ProcessUpload(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(2)] UploadId UploadId
) : ISessionCommand<MediaContent>, IApiCommand;
