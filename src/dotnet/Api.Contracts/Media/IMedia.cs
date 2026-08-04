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

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_ReserveMedia(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] string Scope
) : ISessionCommand<MediaId>, IApiCommand
{
    [DataMember, Key(2)] public MetadataBag Metadata { get; init; } = MetadataBag.Empty;
    [DataMember, Key(3)] public MediaKind Kind { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_RemoveMedia(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] MediaId MediaId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_UpdateProgress(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] MediaId MediaId,
    [property: DataMember, Key(2)] long? ExpectedVersion,
    [property: DataMember, Key(3)] MediaProcessingStage Stage,
    [property: DataMember, Key(4)] double StageProgress,
    [property: DataMember, Key(5)] string? Error = null
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_ProcessUpload(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] MediaId MediaId,
    [property: DataMember, Key(2)] UploadId UploadId
) : ISessionCommand<MediaRef>, IApiCommand;
