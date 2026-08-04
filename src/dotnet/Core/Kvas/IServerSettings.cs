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
public partial record ServerSettings_Set(
    [property: DataMember(Order = 0), Key(0)] Session Session,
    [property: DataMember(Order = 1), Key(1)] string Key,
    [property: DataMember(Order = 2), Key(2)] byte[]? Value
) : ISessionCommand<Unit>, IApiCommand;
