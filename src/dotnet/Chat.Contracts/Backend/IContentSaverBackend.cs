using MemoryPack;

namespace ActualChat.Chat;

public interface IContentSaverBackend : IComputeService
{
    [CommandHandler]
    Task SaveContent(ContentSaverBackend_SaveContent command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContentSaverBackend_SaveContent(
    [property: DataMember, MemoryPackOrder(0)] Symbol ContentId,
    [property: DataMember, MemoryPackOrder(1)] byte[] Content,
    [property: DataMember, MemoryPackOrder(2)] string ContentType
) : ICommand<Unit>, IBackendCommand;
