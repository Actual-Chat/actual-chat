using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

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
    public Task<UserInfo?> Get(UserId userId, CancellationToken cancellationToken)
        => _userInfos.Get(userId, cancellationToken);

    [HttpGet, Publish]
    public Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken)
        => _userInfos.GetByName(name, cancellationToken);
}
