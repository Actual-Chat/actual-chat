using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

public interface IEmailsBackend : IComputeService, IBackendService
{
    [CommandHandler]
    Task<Unit> OnSendDigest(EmailsBackend_SendDigest command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record EmailsBackend_SendDigest(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
