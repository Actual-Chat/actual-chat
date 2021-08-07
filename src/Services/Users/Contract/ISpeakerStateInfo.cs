using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;

namespace ActualChat.Users
{
    public interface ISpeakerStateInfo
    {
        [ComputeMethod(KeepAliveTime = 10)]
        Task<bool> IsOnline(string speakerId, CancellationToken cancellationToken = default);
    }
}
