using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class AccountsController : ControllerBase, IAccounts
{
    private IAccounts Service { get; }
    private ICommander Commander { get; }

    public AccountsController(IAccounts service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<AccountFull?> GetOwn(Session session, CancellationToken cancellationToken)
        => Service.GetOwn(session, cancellationToken);

    [HttpGet, Publish]
    public Task<Account?> Get(Session session, UserId userId, CancellationToken cancellationToken)
        => Service.Get(session, userId, cancellationToken);

    [HttpGet, Publish]
    public Task<AccountFull?> GetFull(Session session, UserId userId, CancellationToken cancellationToken)
        => Service.GetFull(session, userId, cancellationToken);

    [HttpPost]
    public Task Update([FromBody] IAccounts.UpdateCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
