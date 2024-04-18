using MemoryPack;

namespace ActualChat.Users;

public interface ISystemProperties : IComputeService
{
    // Not a [ComputeMethod]!
    Task<double> GetTime(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<string> GetApiVersion(CancellationToken cancellationToken);

    [CommandHandler]
    public Task OnInvalidateEverything(SystemProperties_InvalidateEverything command, CancellationToken cancellationToken);
    [CommandHandler]
    public Task OnPruneComputedGraph(SystemProperties_PruneComputedGraph command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SystemProperties_InvalidateEverything(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] bool Everywhere = false
) : ISessionCommand<Unit>; // NOTE(AY): Maybe add backend & implement IApiCommand?

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record SystemProperties_PruneComputedGraph(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] bool Everywhere = false
) : ISessionCommand<Unit>; // NOTE(AY): Maybe add backend & implement IApiCommand?
