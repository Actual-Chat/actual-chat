namespace ActualChat.Users;

/// <summary>
/// Service for system properties, version checking, and maintenance operations.
/// </summary>
public interface ISystemProperties : IComputeService
{
    // Not a [ComputeMethod]!
    Task<double> GetTime(CancellationToken cancellationToken);
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<ServerApiInfo> GetServerApiInfo(string expectedVersion, CancellationToken cancellationToken);
    Task<ServerApiInfo> GetServerApiInfoNC(string expectedVersion, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnInvalidateEverything(SystemProperties_InvalidateEverything command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnPruneComputedGraph(SystemProperties_PruneComputedGraph command, CancellationToken cancellationToken);

    // Legacy methods - to be removed in the future

    [Obsolete("2025.06: Retired in favour of GetServerApiInfo.")]
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<string> GetApiVersion(CancellationToken cancellationToken);

    [Obsolete("2025.06: Retired in favour of GetServerApiInfo.")]
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.NoCache)]
    Task<SystemProperties_LegacyClientCompatibility> CheckClientCompatibility(string clientVersion, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SystemProperties_InvalidateEverything(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] bool Everywhere = false
) : ISessionCommand<Unit>; // NOTE(AY): Maybe add backend & implement IApiCommand?

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SystemProperties_PruneComputedGraph(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] bool Everywhere = false
) : ISessionCommand<Unit>; // NOTE(AY): Maybe add backend & implement IApiCommand?

/// <summary>
/// Server API version and compatibility information.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: ConstructorShape, MemoryPackConstructor, JsonConstructor]
public sealed partial record ServerApiInfo(
    [property: DataMember, MemoryPackOrder(0), Key(0)] CompatibilityLevel CompatibilityLevel,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string VersionString,
    [property: DataMember, MemoryPackOrder(2), Key(2)] string FullVersionString,
    [property: DataMember, MemoryPackOrder(3), Key(3)] string DisplayVersionString)
{
    public ServerApiInfo(CompatibilityLevel compatibilityLevel)
        : this(compatibilityLevel, ApiConstants.VersionString, ApiConstants.FullVersionString, ApiConstants.DisplayVersionString)
    { }
}

[Obsolete("2025.06: Retired in favour of GetServerApiInfo.")]
public enum SystemProperties_LegacyClientCompatibility { Latest, Incompatible, UpgradeAvailable }
