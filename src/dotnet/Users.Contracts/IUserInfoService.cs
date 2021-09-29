using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;

namespace ActualChat.Users
{
    public interface IUserInfoService
    {
        [ComputeMethod(KeepAliveTime = 10)]
        Task<UserInfo?> TryGet(UserId userId, CancellationToken cancellationToken);
        [ComputeMethod(KeepAliveTime = 10)]
        Task<UserInfo?> TryGetByName(string name, CancellationToken cancellationToken);

    }
}
