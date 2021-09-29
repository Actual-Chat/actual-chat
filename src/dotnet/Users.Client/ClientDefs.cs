using System.Threading;
using System.Threading.Tasks;
using RestEase;

namespace ActualChat.Users.Client
{
    [BasePath("userInfo")]
    public interface IUserInfoClientDef
    {
        [Get(nameof(TryGet))]
        Task<UserInfo?> TryGet(UserId userId, CancellationToken cancellationToken);
        [Get(nameof(TryGetByName))]
        Task<UserInfo?> TryGetByName(UserId name, CancellationToken cancellationToken);
    }

    [BasePath("userState")]
    public interface IUserStateClientDef
    {
        [Get(nameof(IsOnline))]
        Task<bool> IsOnline(UserId userId, CancellationToken cancellationToken);
    }
}
