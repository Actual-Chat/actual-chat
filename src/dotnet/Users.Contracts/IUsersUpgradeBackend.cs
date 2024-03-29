using ActualLab.Rpc;

namespace ActualChat.Users;

public interface IUsersUpgradeBackend : IComputeService, IBackendService
{
    // Not a [ComputeMethod]!
    Task<ImmutableList<UserId>> ListAllUserIds(CancellationToken cancellationToken);
}
