using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Media;

public interface IGrabStatusesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<GrabStatus?> Get(Symbol id, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<GrabStatus> OnChange(GrabStatusesBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record GrabStatusesBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] bool IsSuccessful
) : ICommand<GrabStatus>, IBackendCommand, IHasShardKey<Symbol>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol ShardKey => Id;
}
