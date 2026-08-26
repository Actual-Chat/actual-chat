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
    // Not a [ComputeMethod]! The payload must cross the wire on every call - it measures
    // sustained throughput, so a cached or compressible result would prove nothing.
    [RpcMethod(ConnectTimeout = 0.5)]
    Task<byte[]> GetProbePayload(int size, CancellationToken cancellationToken);
    [RpcMethod(RemoteExecutionMode = RpcRemoteExecutionMode.AwaitForConnection, ConnectTimeout = 10)]
    Task ReportRpcEndpoint(RpcEndpointReport report, CancellationToken cancellationToken);
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
/// What a client reports about the RPC endpoint it connected through, so the split
/// between direct and relayed connections can be measured rather than assumed.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record RpcEndpointReport(
    [property: DataMember, Key(0)] string Endpoint,
    [property: DataMember, Key(1)] RpcEndpointReason Reason,
    // Negative where the probe didn't run or didn't finish - a timed-out origin is the
    // case a relay exists for, and it produces no duration at all.
    [property: DataMember, Key(2)] double OriginProbeMs = -1,
    [property: DataMember, Key(3)] double EndpointProbeMs = -1);

public enum RpcEndpointReason
{
    Retained = 0,
    Measured,
    Unmeasurable,
    Demoted,
}

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
