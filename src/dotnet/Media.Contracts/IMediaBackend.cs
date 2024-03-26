using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Media;

public interface IMediaBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    public Task<Media?> Get(MediaId mediaId, CancellationToken cancellationToken);

    [ComputeMethod]
    public Task<Media?> GetByContentId(string contentId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<Media?> OnChange(MediaBackend_Change command, CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnCopyChat(MediaBackend_CopyChat command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MediaBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] MediaId Id,
    [property: DataMember, MemoryPackOrder(1)] Change<Media> Change
) : ICommand<Media?>, IBackendCommand, IHasShardKey<MediaId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public MediaId ShardKey => Id;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MediaBackend_CopyChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] string CorrelationId,
    [property: DataMember, MemoryPackOrder(2)] MediaId[] MediaIds
) : ICommand<Unit>, IBackendCommand;
