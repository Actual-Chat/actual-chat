using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Service for system properties, version checking, and maintenance operations.
/// </summary>
public interface ISystemProperties : IComputeService
{
    // Not a [ComputeMethod]! ConnectTimeout stops a clock probe from parking until reconnect.
    [RpcMethod(ConnectTimeout = 0.5)]
    Task<double> GetTime(CancellationToken cancellationToken);
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ServerApiInfo> GetServerApiInfo(string expectedVersion, CancellationToken cancellationToken);
    Task<ServerApiInfo> GetServerApiInfoNC(string expectedVersion, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnInvalidateEverything(SystemProperties_InvalidateEverything command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnPruneComputedGraph(SystemProperties_PruneComputedGraph command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record SystemProperties_InvalidateEverything(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] bool Everywhere = false
) : ISessionCommand<Unit>; // NOTE(AY): Maybe add backend & implement IApiCommand?

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record SystemProperties_PruneComputedGraph(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] bool Everywhere = false
) : ISessionCommand<Unit>; // NOTE(AY): Maybe add backend & implement IApiCommand?

/// <summary>
/// Server API version and compatibility information.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor, JsonConstructor]
public sealed partial record ServerApiInfo(
    [property: DataMember, Key(0)] CompatibilityLevel CompatibilityLevel,
    [property: DataMember, Key(1)] string VersionString,
    [property: DataMember, Key(2)] string FullVersionString,
    [property: DataMember, Key(3)] string DisplayVersionString,
    [property: DataMember, Key(4)] string MinReportableClientVersion = "")
{
    public ServerApiInfo(CompatibilityLevel compatibilityLevel)
        : this(compatibilityLevel,
            ApiConstants.VersionString,
            ApiConstants.FullVersionString,
            ApiConstants.DisplayVersionString,
            "")
    { }
}
