using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;

namespace ActualChat.Users
{
    public interface IUserStateService
    {
        [ComputeMethod(KeepAliveTime = 30)]
        Task<bool> IsOnline(string userId, CancellationToken cancellationToken);
    }
}
