using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Backend service for sending email notifications and digests.
/// </summary>
public interface IEmailsBackend : IComputeService, IBackendService
{
    [CommandHandler]
    Task<Unit> OnSendDigest(EmailsBackend_SendDigest command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to send a digest email to a user.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record EmailsBackend_SendDigest(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>, IHasTimeout
{
    [property: DataMember, MemoryPackOrder(1)]
    public bool IsDiagnosticsEnabled { get; init; }

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;

    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(5);
}
