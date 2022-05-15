using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserContactsController : ControllerBase, IUserContacts
{
    private readonly IUserContacts _service;

    public UserContactsController(IUserContacts service)
        => _service = service;

    [HttpGet, Publish]
    public Task<ImmutableArray<UserContact>> GetAll(Session session, CancellationToken cancellationToken)
        => _service.GetAll(session, cancellationToken);
}
