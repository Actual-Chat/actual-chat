using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;

namespace ActualChat.Users
{
    public interface ISpeakerService
    {
        [ComputeMethod(KeepAliveTime = 10)]
        Task<Speaker?> TryGet(string userId, CancellationToken cancellationToken = default);
        [ComputeMethod(KeepAliveTime = 10)]
        Task<Speaker?> TryGetByName(string name, CancellationToken cancellationToken = default);

    }
}
