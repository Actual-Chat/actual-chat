using MemoryPack;

namespace ActualChat.Kvas;

public interface IServerKvas : IComputeService
{
    [ComputeMethod, ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache, MinCacheDuration = 600)]
    Task<byte[]?> Get(Session session, string key, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task OnSet(ServerKvas_Set command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnSetMany(ServerKvas_SetMany command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnMigrateGuestKeys(ServerKvas_MigrateGuestKeys command, CancellationToken cancellationToken = default);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record ServerKvas_Set(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Session Session,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string Key,
    [property: DataMember(Order = 2), MemoryPackOrder(2)] byte[]? Value
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record ServerKvas_SetMany(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Session Session,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] params (string Key, byte[]? Value)[] Items
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record ServerKvas_MigrateGuestKeys(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Session Session
) : ISessionCommand<Unit>, IApiCommand;
