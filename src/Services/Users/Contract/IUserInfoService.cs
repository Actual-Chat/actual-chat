using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;

namespace ActualChat.Users
{
    public interface IUserInfoService
    {
        [ComputeMethod(KeepAliveTime = 10)]
        Task<UserInfo?> TryGet(string userId, CancellationToken cancellationToken = default);
        [ComputeMethod(KeepAliveTime = 10)]
        Task<UserInfo?> TryGetByName(string name, CancellationToken cancellationToken = default);

    }
}
