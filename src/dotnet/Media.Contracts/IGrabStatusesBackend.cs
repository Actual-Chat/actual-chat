using ActualLab.Rpc;

namespace ActualChat.Media;

/// <summary>
/// Backend service for tracking link preview grab operation statuses.
/// </summary>
public interface IGrabStatusesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<GrabStatus?> Get(Symbol id, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<GrabStatus> OnChange(GrabStatusesBackend_Change command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to update the status of a link preview grab operation.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record GrabStatusesBackend_Change(
    [property: DataMember, Key(0)] Symbol Id,
    [property: DataMember, Key(1)] bool IsSuccessful
) : ICommand<GrabStatus>, IBackendCommand, IHasShardKey<Symbol>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Symbol ShardKey => Id;
}
