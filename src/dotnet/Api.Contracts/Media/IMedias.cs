using MemoryPack;

namespace ActualChat.Media;

public interface IMedias : IComputeService
{
    [CommandHandler]
    Task<MediaId> OnReserveMedia(Medias_ReserveMedia command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Medias_ReserveMedia(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string Scope
) : ISessionCommand<MediaId>, IApiCommand;
