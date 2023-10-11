using MemoryPack;

namespace ActualChat.Chat;

public interface ILinkPreviewsBackend : IComputeService
{
    [ComputeMethod]
    Task<LinkPreview?> Get(Symbol id, CancellationToken cancellationToken);

    [CommandHandler]
    Task<LinkPreview?> OnRefresh(LinkPreviewsBackend_Refresh command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record LinkPreviewsBackend_Refresh(
    [property: DataMember, MemoryPackOrder(0)] string Url
) : ICommand<LinkPreview?>, IBackendCommand;
