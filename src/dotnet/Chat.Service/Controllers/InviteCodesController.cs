using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class InviteCodesController : ControllerBase, IInviteCodes
{
    private readonly IInviteCodes _inviteCodes;
    private readonly ISessionResolver _sessionResolver;

    public InviteCodesController(IInviteCodes inviteCodes, ISessionResolver sessionResolver)
    {
        _inviteCodes = inviteCodes;
        _sessionResolver = sessionResolver;
    }

    // Commands

    [HttpGet, Publish]
    public Task<ImmutableArray<InviteCode>> Get(Session session, string chatId, CancellationToken cancellationToken)
        => _inviteCodes.Get(session, chatId, cancellationToken);

    [HttpPost]
    public Task<InviteCode> Generate(
        [FromBody] IInviteCodes.GenerateCommand command,
        CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _inviteCodes.Generate(command, cancellationToken);
    }

    [HttpPost]
    public Task<InviteCodeUseResult> UseInviteCode(
        [FromBody] IInviteCodes.UseInviteCodeCommand command,
        CancellationToken cancellationToken)
    {
        command.UseDefaultSession(_sessionResolver);
        return _inviteCodes.UseInviteCode(command, cancellationToken);
    }
}
