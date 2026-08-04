namespace ActualChat.Users;

/// <summary>
/// Session-scoped account settings store that transmits <see cref="StoredSettings"/>
/// instead of raw bytes.
/// </summary>
public interface IUserSettings : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<StoredSettings?> Get(Session session, string key, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task OnSet(UserSettings_Set command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Command to set a single settings value in the account settings store.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public partial record UserSettings_Set(
    [property: DataMember(Order = 0), Key(0)] Session Session,
    [property: DataMember(Order = 1), Key(1)] string Key,
    [property: DataMember(Order = 2), Key(2)] StoredSettings? Value
) : ISessionCommand<Unit>, IApiCommand;
