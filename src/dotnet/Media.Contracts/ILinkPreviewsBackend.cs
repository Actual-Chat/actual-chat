using ActualChat.Chat.Events;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Media;

public interface ILinkPreviewsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<LinkPreview?> GetForEntry(ChatEntryId entryId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<LinkPreview?> OnRefresh(LinkPreviewsBackend_Refresh command, CancellationToken cancellationToken);
    [EventHandler]
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record LinkPreviewsBackend_Refresh(
    [property: DataMember, MemoryPackOrder(0)] string Url
) : ICommand<LinkPreview?>, IBackendCommand, IHasShardKey<string>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string ShardKey => Url;
}
