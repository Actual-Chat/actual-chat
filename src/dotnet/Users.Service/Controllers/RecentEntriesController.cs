using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Server;

namespace ActualChat.Users.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors, UseDefaultSession]
public class RecentEntriesController : ControllerBase, IRecentEntries
{
    private IRecentEntries Service { get; }
    private ICommander Commander { get; }

    public RecentEntriesController(IRecentEntries service, ICommander commander)
    {
        Service = service;
        Commander = commander;
    }

    [HttpGet, Publish]
    public Task<ImmutableHashSet<string>> ListUserContactIds(
        Session session,
        int limit,
        CancellationToken cancellationToken)
        => Service.ListUserContactIds(session, limit, cancellationToken);

    [HttpPost]
    public Task<RecentEntry?> Update(IRecentEntries.UpdateUserContactCommand command, CancellationToken cancellationToken)
        => Commander.Call(command, cancellationToken);
}
