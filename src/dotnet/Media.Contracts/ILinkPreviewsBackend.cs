using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Media;

public interface ILinkPreviewsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<LinkPreview?> Get(
        Symbol id,
        bool mustScheduleRefreshIfRequired,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<LinkPreview?> OnChange(LinkPreviewsBackend_Change command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record LinkPreviewsBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<LinkPreview> Change
) : ICommand<LinkPreview?>, IBackendCommand, IHasShardKey<Symbol>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol ShardKey => Id;
}
