using ActualLab.Rpc;

namespace ActualChat.Media;

public interface IMediaProgressBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<MediaProgress?> Get(MediaId? mediaId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<MediaProgress?> OnChange(MediaProgressBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record MediaProgressBackend_Change(
    [property: DataMember, Key(0)] MediaId Id,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<MediaProgress> Change
) : ICommand<MediaProgress?>, IBackendCommand, IHasShardKey<MediaId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public MediaId ShardKey => Id;
}
