using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Backend service for server-side key-value storage operations.
/// </summary>
public interface IServerKvasBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<byte[]?> Get(string prefix, string key, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<ApiList<(string Key, byte[] Value)>> List(string prefix, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task OnSetMany(ServerKvasBackend_SetMany command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Command to set multiple key-value pairs at once.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record ServerKvasBackend_SetMany(
    [property: DataMember(Order = 0), MemoryPackOrder(0), NbKey(0)] string Prefix,
    [property: DataMember(Order = 1), MemoryPackOrder(1), NbKey(1)] params (string Key, byte[]? Value)[] Items
) : ICommand<Unit>, IBackendCommand, IHasShardKey<string>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public string ShardKey => Prefix;
}
