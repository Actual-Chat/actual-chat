using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class InviteCodesController : ControllerBase, IInviteCodes
{
    private readonly IInviteCodes _service;

    public InviteCodesController(IInviteCodes service)
        => _service = service;

    // Commands

    [HttpGet, Publish]
    public Task<ImmutableArray<InviteCode>> Get(Session session, string chatId, CancellationToken cancellationToken)
        => _service.Get(session, chatId, cancellationToken);

    [HttpPost]
    public Task<InviteCode> Generate(
        [FromBody] IInviteCodes.GenerateCommand command,
        CancellationToken cancellationToken)
        => _service.Generate(command, cancellationToken);

    [HttpPost]
    public Task<InviteCodeUseResult> UseInviteCode(
        [FromBody] IInviteCodes.UseInviteCodeCommand command,
        CancellationToken cancellationToken)
        => _service.UseInviteCode(command, cancellationToken);
}
