using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserInfosController : ControllerBase, IUserInfos
{
    private readonly IUserInfos _userInfos;
    private readonly ISessionResolver _sessionResolver;

    public UserInfosController(IUserInfos userInfos, ISessionResolver sessionResolver)
    {
        _userInfos = userInfos;
        _sessionResolver = sessionResolver;
    }

    [HttpGet, Publish]
    public Task<UserInfo?> Get(string userId, CancellationToken cancellationToken)
        => _userInfos.Get(userId, cancellationToken);

    [HttpGet, Publish]
    public Task<UserInfo?> GetByName(string name, CancellationToken cancellationToken)
        => _userInfos.GetByName(name, cancellationToken);

    [HttpGet, Publish]
    public Task<string?> GetGravatarHash(string userId, CancellationToken cancellationToken)
        => _userInfos.GetGravatarHash(userId, cancellationToken);

    [HttpGet, Publish]
    public Task<bool> IsAdmin(string userId, CancellationToken cancellationToken)
        => _userInfos.IsAdmin(userId, cancellationToken);
}
