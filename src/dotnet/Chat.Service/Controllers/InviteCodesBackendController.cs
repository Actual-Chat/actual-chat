using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class InviteCodesBackendController : ControllerBase, IInviteCodesBackend
{
    private readonly IInviteCodesBackend _service;

    public InviteCodesBackendController(IInviteCodesBackend service)
        => _service = service;

    public Task<InviteCode?> GetByValue(string inviteCode, CancellationToken cancellationToken)
        => _service.GetByValue(inviteCode, cancellationToken);

    public Task<ImmutableArray<InviteCode>> Get(string chatId, string userId, CancellationToken cancellationToken)
        => _service.Get(chatId, userId, cancellationToken);

    public Task<bool> CheckIfInviteCodeUsed(Session session, string chatId, CancellationToken cancellationToken)
        => _service.CheckIfInviteCodeUsed(session, chatId, cancellationToken);

    public Task<InviteCode> Generate(IInviteCodesBackend.GenerateCommand command, CancellationToken cancellationToken)
        => _service.Generate(command, cancellationToken);

    public Task UseInviteCode(IInviteCodesBackend.UseInviteCodeCommand command, CancellationToken cancellationToken)
        => _service.UseInviteCode(command, cancellationToken);
}
