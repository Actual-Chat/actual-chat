using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class MentionsController : ControllerBase, IMentions
{
    private readonly IMentions _service;

    public MentionsController(IMentions service)
        => _service = service;

    [HttpGet, Publish]
    public Task<Mention?> GetLastOwn(
        Session session,
        Symbol chatId,
        CancellationToken cancellationToken)
        => _service.GetLastOwn(session, chatId, cancellationToken);
}
