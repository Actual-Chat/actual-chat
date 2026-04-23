using ActualLab.Rpc;

namespace ActualChat.Media;

public interface IMediaProgressBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<MediaProgress?> Get(MediaId? mediaId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<MediaProgress?> OnChange(MediaProgressBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MediaProgressBackend_Change(
    [property: DataMember, MemoryPackOrder(0), NbKey(0)] MediaId Id,
    [property: DataMember, MemoryPackOrder(1), NbKey(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2), NbKey(2)] Change<MediaProgress> Change
) : ICommand<MediaProgress?>, IBackendCommand, IHasShardKey<MediaId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public MediaId ShardKey => Id;
}
