using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class ContactsController : ControllerBase, IContacts
{
    private IContacts Service { get; }
    private ICommander Commander { get; }

    public ContactsController(IContacts service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<ImmutableArray<Contact>> List(
        Session session,
        CancellationToken cancellationToken)
        => Service.List(session, cancellationToken);

    [HttpGet, Publish]
    public Task<Contact?> Get(Session session, string contactId, CancellationToken cancellationToken)
        => Service.Get(session, contactId, cancellationToken);

    public Task<Contact> Change(IContacts.ChangeCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
