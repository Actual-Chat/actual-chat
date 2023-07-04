using MemoryPack;

namespace ActualChat.Users;

public interface IServerKvasBackend : IComputeService
{
    [ComputeMethod]
    Task<byte[]?> Get(string prefix, string key, CancellationToken cancellationToken = default);
    [ComputeMethod]
    Task<ApiList<(string Key, byte[] Value)>> List(string prefix, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task OnSetMany(ServerKvasBackend_SetMany command, CancellationToken cancellationToken = default);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public partial record ServerKvasBackend_SetMany(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] string Prefix,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] params (string Key, byte[]? Value)[] Items
) : ICommand<Unit>, IBackendCommand;
