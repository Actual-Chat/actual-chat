using MemoryPack;

namespace ActualChat.Media;

public interface IMediaBackend : IComputeService
{
    [ComputeMethod]
    public Task<Media?> Get(MediaId mediaId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<Media?> OnChange(MediaBackend_Change command, CancellationToken cancellationToken);
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
