using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Backend service for user data migration and upgrade operations.
/// </summary>
public interface IUsersUpgradeBackend : IComputeService, IBackendService
{
    // Not a [ComputeMethod]!
    Task<ImmutableList<UserId>> ListAllUserIds(CancellationToken cancellationToken);
}
