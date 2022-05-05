using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserProfilesBackendController : ControllerBase, IUserProfilesBackend
{
    private readonly IUserProfilesBackend _service;

    public UserProfilesBackendController(IUserProfilesBackend service)
        => _service = service;

    [HttpGet, Publish]
    public Task<UserProfile?> Get(string id, CancellationToken cancellationToken)
        => _service.Get(id, cancellationToken);

    [HttpGet, Publish]
    public Task<UserProfile?> GetByName(string name, CancellationToken cancellationToken)
        => _service.GetByName(name, cancellationToken);

    [HttpPost]
    public Task Update([FromBody] IUserProfilesBackend.UpdateCommand command, CancellationToken cancellationToken)
        => _service.Update(command, cancellationToken);

    [HttpPost]
    public Task Create(IUserProfilesBackend.CreateCommand command, CancellationToken cancellationToken)
        => _service.Create(command, cancellationToken);
}
