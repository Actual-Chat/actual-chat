namespace ActualChat.Users;

/// <summary>
/// Session-scoped account settings store that transmits <see cref="StoredSettings"/>
/// instead of raw bytes.
/// </summary>
public interface IAccountSettings : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<StoredSettings?> Get(Session session, string key, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task OnSet(AccountSettings_Set command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Command to set a single settings value in the account settings store.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public partial record AccountSettings_Set(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Session Session,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string Key,
    [property: DataMember(Order = 2), MemoryPackOrder(2)] StoredSettings? Value
) : ISessionCommand<Unit>, IApiCommand;
