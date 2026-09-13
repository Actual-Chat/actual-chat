using ActualLab.Rpc;

namespace ActualChat.Users;

public interface IPasskeysBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<PasskeyCredential?> Get(UserId userId, string id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<PasskeyCredential>> List(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<PasskeyCredential?> OnChange(PasskeysBackend_Change command, CancellationToken cancellationToken);
}

/// <summary>
/// A stored passkey with its key material. <see cref="Passkey"/> is the user-facing projection.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record PasskeyCredential(
    [property: DataMember, Key(0)] string Id,
    [property: DataMember, Key(1)] UserId UserId
) {
    [DataMember, Key(2)] public byte[] UserHandle { get; init; } = [];
    [DataMember, Key(3)] public byte[] PublicKey { get; init; } = [];
    [DataMember, Key(4)] public uint SignCount { get; init; }
    [DataMember, Key(5)] public Guid Aaguid { get; init; }
    [DataMember, Key(6)] public string Transports { get; init; } = "";
    [DataMember, Key(7)] public bool IsBackupEligible { get; init; }
    [DataMember, Key(8)] public bool IsBackedUp { get; init; }
    [DataMember, Key(9)] public string Name { get; init; } = "";
    [DataMember, Key(10)] public Moment CreatedAt { get; init; }
    [DataMember, Key(11)] public Moment? LastUsedAt { get; init; }

    public Passkey ToPasskey()
        => new(Id, Name, CreatedAt, LastUsedAt, IsBackupEligible);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeysBackend_Change(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] string Id,
    [property: DataMember, Key(2)] Change<PasskeyCredential> Change
) : ICommand<PasskeyCredential?>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}
