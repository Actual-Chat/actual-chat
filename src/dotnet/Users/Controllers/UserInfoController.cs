using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Authentication;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController, JsonifyErrors]
    public class UserInfoController : ControllerBase, IUserInfoService
    {
        private readonly IUserInfoService _userInfos;
        private readonly ISessionResolver _sessionResolver;

        public UserInfoController(IUserInfoService userInfos, ISessionResolver sessionResolver)
        {
            _userInfos = userInfos;
            _sessionResolver = sessionResolver;
        }

        [HttpGet, Publish]
        public Task<UserInfo?> TryGet(string userId, CancellationToken cancellationToken)
            => _userInfos.TryGet(userId, cancellationToken);

        [HttpGet, Publish]
        public Task<UserInfo?> TryGetByName(string name, CancellationToken cancellationToken)
            => _userInfos.TryGetByName(name, cancellationToken);
    }
}
