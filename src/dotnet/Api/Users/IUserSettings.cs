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
public partial record UserSettings_Set : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Key { get; init; }
    [DataMember(Order = 3), Key(3)] public required StoredSettings? Value { get; init; }
}
