using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Chat.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class MentionsController : ControllerBase, IMentions
{
    private IMentions Service { get; }

    public MentionsController(IMentions service)
        => Service = service;

    [HttpGet, Publish]
    public Task<Mention?> GetLastOwn(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
        => Service.GetLastOwn(session, chatId, cancellationToken);
}
