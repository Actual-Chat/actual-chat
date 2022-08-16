using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class UserContactsController : ControllerBase, IUserContacts
{
    private IUserContacts Service { get; }
    private ICommander Commander { get; }

    public UserContactsController(IUserContacts service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<ImmutableArray<UserContact>> List(
        Session session,
        CancellationToken cancellationToken)
        => Service.List(session, cancellationToken);

    public Task<UserContact?> Change(IUserContacts.ChangeCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
