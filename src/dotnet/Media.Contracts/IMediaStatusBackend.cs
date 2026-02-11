using ActualLab.Rpc;

namespace ActualChat.Media;

public interface IMediaStatusBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<MediaStatusInfo?> Get(MediaId? mediaId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<MediaStatusInfo?> OnChange(MediaStatusBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record MediaStatusBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] MediaId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<MediaStatusInfo> Change
) : ICommand<MediaStatusInfo?>, IBackendCommand, IHasShardKey<MediaId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public MediaId ShardKey => Id;
}
