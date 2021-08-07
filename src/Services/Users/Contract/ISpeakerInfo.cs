using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;

namespace ActualChat.Users
{
    public interface ISpeakerInfo
    {
        [ComputeMethod(KeepAliveTime = 10)]
        Task<Speaker?> TryGet(string id, CancellationToken cancellationToken = default);
        [ComputeMethod(KeepAliveTime = 10)]
        Task<Speaker?> TryGetByName(string name, CancellationToken cancellationToken = default);

    }
}
