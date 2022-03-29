using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class UserContactsController : ControllerBase, IUserContacts
{
    private readonly IUserContacts _userContacts;

    public UserContactsController(IUserContacts userContacts)
        => _userContacts = userContacts;

    [HttpGet, Publish]
    public Task<ImmutableArray<UserContact>> GetContacts(Session session, CancellationToken cancellationToken)
        => _userContacts.GetContacts(session, cancellationToken);
}
