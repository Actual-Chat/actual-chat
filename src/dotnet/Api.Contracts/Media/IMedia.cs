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
    [CommandHandler]
    Task<MediaRef?> OnGenerate(Media_Generate command, CancellationToken cancellationToken);
}

// Makes an image rather than taking one, and stores it in Scope exactly as an upload would.
// Returns null when the provider declines the description or is not configured.
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_Generate : ApiCommand<MediaRef?>
{
    [DataMember(Order = 2), Key(2)] public required string Scope { get; init; }
    [DataMember(Order = 3), Key(3)] public required string Description { get; init; }
    [DataMember(Order = 4), Key(4)] public ImageStyle Style { get; init; } = ImageStyle.Default;
    [DataMember(Order = 5), Key(5)] public MediaKind Kind { get; init; } = MediaKind.ChatPicture;
    [DataMember(Order = 6), Key(6)] public bool IsBackground { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_ReserveMedia : ApiCommand<MediaId>
{
    [DataMember(Order = 2), Key(2)] public required string Scope { get; init; }
    [DataMember(Order = 3), Key(3)] public MetadataBag Metadata { get; init; } = MetadataBag.Empty;
    [DataMember(Order = 4), Key(4)] public MediaKind Kind { get; init; }
    [DataMember(Order = 5), Key(5)] public byte[]? Placeholder { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_RemoveMedia : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required MediaId MediaId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_UpdateProgress : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required MediaId MediaId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long? ExpectedVersion { get; init; }
    [DataMember(Order = 4), Key(4)] public required MediaProcessingStage Stage { get; init; }
    [DataMember(Order = 5), Key(5)] public required double StageProgress { get; init; }
    [DataMember(Order = 6), Key(6)] public string? Error { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Media_ProcessUpload : ApiCommand<MediaRef>
{
    [DataMember(Order = 2), Key(2)] public required MediaId MediaId { get; init; }
    [DataMember(Order = 3), Key(3)] public required UploadId UploadId { get; init; }
}
