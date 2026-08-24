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

// Not deduplicated: a settings write is last-write-wins, so a repeat is harmless, while settings
// churn - every toggle, every remembered panel size - would hold an entry per write.

/// <summary>
/// Command to set a single settings value in the account settings store.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public partial record UserSettings_Set : ApiCommand<Unit>, INotDeduplicated
{
    [DataMember(Order = 2), Key(2)] public required string Key { get; init; }
    [DataMember(Order = 3), Key(3)] public required StoredSettings? Value { get; init; }
}
