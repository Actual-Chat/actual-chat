using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Media;

public interface IMediaStatusBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<MediaStatusInfo?> Get(MediaId? mediaId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<MediaStatusInfo?> OnChange(MediaStatusBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MediaStatusBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] MediaId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<MediaStatusInfo> Change
) : ICommand<MediaStatusInfo?>, IBackendCommand, IHasShardKey<MediaId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public MediaId ShardKey => Id;
}
