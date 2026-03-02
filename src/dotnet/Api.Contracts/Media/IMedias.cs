namespace ActualChat.Media;

public interface IMedias : IComputeService
{
    [ComputeMethod]
    Task<MediaProgress?> GetProgress(Session session, MediaId mediaId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<MediaContent?> GetContent(Session session, MediaId mediaId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<MediaId> OnReserveMedia(Medias_ReserveMedia command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveMedia(Medias_RemoveMedia command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpdateProgress(Medias_UpdateProgress command, CancellationToken cancellationToken);
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
    [DataMember, MemoryPackOrder(3)] public MediaKind Kind { get; init; }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Medias_RemoveMedia(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] MediaId MediaId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Medias_UpdateProgress(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] MediaStage Stage,
    [property: DataMember, MemoryPackOrder(4)] double StageProgress,
    [property: DataMember, MemoryPackOrder(5)] string ErrorMessage
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Medias_ProcessUpload(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(2)] UploadId UploadId
) : ISessionCommand<MediaContent>, IApiCommand;
