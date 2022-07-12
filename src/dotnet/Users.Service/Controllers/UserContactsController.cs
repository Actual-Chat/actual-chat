using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class UserContactsController : ControllerBase, IUserContacts
{
    private readonly IUserContacts _service;
    private readonly ICommander _commander;

    public UserContactsController(IUserContacts service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<ImmutableArray<UserContact>> GetAll(Session session, CancellationToken cancellationToken)
        => _service.GetAll(session, cancellationToken);
}
