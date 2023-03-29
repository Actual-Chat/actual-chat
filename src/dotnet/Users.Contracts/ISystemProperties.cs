using ActualChat.Hosting;

namespace ActualChat.Users;

public interface ISystemProperties : IComputeService
{
    // Not a [ComputeMethod]!
    Task<double> GetTime(CancellationToken cancellationToken);
    // Not a [ComputeMethod]!
    Task<string?> GetMinClientVersion(AppKind appKind, CancellationToken cancellationToken);
}
