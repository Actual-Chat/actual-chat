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
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(Internal.ServerKvas_SetMessagePackFormatter))]
// ReSharper disable once InconsistentNaming
public partial record ServerKvas_Set : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Key { get; init; }
    [DataMember(Order = 3), Key(3)] public required byte[]? Value { get; init; }
    // Both are far above any settings record this store is meant to hold
    public const int MaxKeyLength = 1024;
    public const int MaxValueLength = 64 * 1024;
}

/// <summary>
/// Command to set multiple key-value pairs in the server KVAS.
/// </summary>
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(Internal.ServerKvas_SetManyMessagePackFormatter))]
// ReSharper disable once InconsistentNaming
public partial record ServerKvas_SetMany : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required (string Key, byte[]? Value)[] Items { get; init; }
    public const int MaxItemCount = 128;
}

/// <summary>
/// Command to migrate guest session keys to an authenticated session.
/// </summary>
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(Internal.ServerKvas_MigrateGuestKeysMessagePackFormatter))]
// ReSharper disable once InconsistentNaming
public partial record ServerKvas_MigrateGuestKeys : ApiCommand<Unit>;
