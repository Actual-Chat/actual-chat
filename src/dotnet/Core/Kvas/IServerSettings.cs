using MemoryPack;

namespace ActualChat.Kvas;

public interface IServerSettings : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<byte[]?> Get(Session session, string key, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task OnSet(ServerSettings_Set command, CancellationToken cancellationToken = default);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record ServerSettings_Set(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Session Session,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string Key,
    [property: DataMember(Order = 2), MemoryPackOrder(2)] byte[]? Value
) : ISessionCommand<Unit>, IApiCommand;
