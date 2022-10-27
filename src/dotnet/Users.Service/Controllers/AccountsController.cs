using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class AccountsController : ControllerBase, IAccounts
{
    private readonly IAccounts _service;
    private readonly ICommander _commander;

    public AccountsController(IAccounts service, ICommander commander)
    {
        _service = service;
        _commander = commander;
    }

    [HttpGet, Publish]
    public Task<Account?> Get(Session session, CancellationToken cancellationToken)
        => _service.Get(session, cancellationToken);

    [HttpGet, Publish]
    public Task<Account?> GetByUserId(Session session, string userId, CancellationToken cancellationToken)
        => _service.GetByUserId(session, userId, cancellationToken);

    [HttpPost]
    public Task Update([FromBody] IAccounts.UpdateCommand command, CancellationToken cancellationToken)
        => _commander.Call(command, cancellationToken);
}
