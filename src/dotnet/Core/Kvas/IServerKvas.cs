namespace ActualChat.Kvas;

/// <summary>
/// Server-side key-value store service for session-scoped settings.
/// </summary>
public interface IServerKvas : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<byte[]?> Get(Session session, string key, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task OnSet(ServerKvas_Set command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnSetMany(ServerKvas_SetMany command, CancellationToken cancellationToken = default);
    [CommandHandler]
    Task OnMigrateGuestKeys(ServerKvas_MigrateGuestKeys command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Command to set a single key-value pair in the server KVAS.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[MessagePackFormatter(typeof(Internal.ServerKvas_SetMessagePackFormatter))]
// ReSharper disable once InconsistentNaming
public partial record ServerKvas_Set(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] string Key,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] byte[]? Value
) : ISessionCommand<Unit>, IApiCommand
{
    // Both are far above any settings record this store is meant to hold
    public const int MaxKeyLength = 1024;
    public const int MaxValueLength = 64 * 1024;
}

/// <summary>
/// Command to set multiple key-value pairs in the server KVAS.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[MessagePackFormatter(typeof(Internal.ServerKvas_SetManyMessagePackFormatter))]
// ReSharper disable once InconsistentNaming
public partial record ServerKvas_SetMany(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] params (string Key, byte[]? Value)[] Items
) : ISessionCommand<Unit>, IApiCommand
{
    public const int MaxItemCount = 128;
}

/// <summary>
/// Command to migrate guest session keys to an authenticated session.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[MessagePackFormatter(typeof(Internal.ServerKvas_MigrateGuestKeysMessagePackFormatter))]
// ReSharper disable once InconsistentNaming
public partial record ServerKvas_MigrateGuestKeys(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] Session Session
) : ISessionCommand<Unit>, IApiCommand;
