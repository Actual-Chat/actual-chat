using ActualLab.Rpc;

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
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or delete a link preview.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record LinkPreviewsBackend_Change(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Change<LinkPreview> Change
) : ICommand<LinkPreview?>, IBackendCommand, IHasShardKey<Symbol>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Symbol ShardKey => Id;
}
