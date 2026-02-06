using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Media;

/// <summary>
/// Backend service for generating and caching link preview metadata.
/// </summary>
public interface ILinkPreviewsBackend : IComputeService, IBackendService
{
    [ComputeMethod(AutoInvalidationDelay = 25 * 60 * 60 * 1000)]
    Task<LinkPreview?> Get(
        Symbol id,
        bool tryScheduleRefresh,
        CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<LinkPreview?> OnChange(LinkPreviewsBackend_Change command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or delete a link preview.
/// </summary>
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
