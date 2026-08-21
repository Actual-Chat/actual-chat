namespace ActualChat.Kvas;

/// <summary>
/// Server-side settings storage service.
/// </summary>
public interface IServerSettings : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<byte[]?> Get(Session session, string key, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task OnSet(ServerSettings_Set command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Command to set a server-side setting value.
/// </summary>
[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(Internal.ServerSettings_SetMessagePackFormatter))]
// ReSharper disable once InconsistentNaming
public partial record ServerSettings_Set : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Key { get; init; }
    [DataMember(Order = 3), Key(3)] public required byte[]? Value { get; init; }
}
