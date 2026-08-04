using ActualLab.Rpc;

namespace ActualChat.Media;

/// <summary>
/// Backend service for managing media files (images, audio, video).
/// </summary>
public interface IMediaBackend : IComputeService, IBackendService
{
    // Media records are effectively immutable once uploaded, hence the longer floor.
    [ComputeMethod(MinCacheDuration = 300)]
    Task<Media?> Get(MediaId? mediaId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<MediaFull?> GetFull(MediaId? mediaId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Media?> GetByMediaIdScope(string mediaIdScope, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Media?> GetByBlobId(string blobId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<MediaFull?> OnChange(MediaBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnCopyChat(MediaBackend_CopyChat command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or delete a media record.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record MediaBackend_Change(
    [property: DataMember, Key(0)] MediaId Id,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<MediaFull> Change
) : ICommand<MediaFull?>, IBackendCommand, IHasShardKey<MediaId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public MediaId ShardKey => Id;
}

/// <summary>
/// Command to copy media files to a new chat.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record MediaBackend_CopyChat(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] string CorrelationId,
    [property: DataMember, Key(2)] MediaId[] MediaIds
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ChatId;
}
