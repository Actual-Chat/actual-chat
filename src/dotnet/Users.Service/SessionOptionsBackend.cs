using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class SessionOptionsBackend : DbServiceBase<UsersDbContext>, ISessionOptionsBackend
{
    private readonly IAuth _auth;
    private readonly ICommander _commander;

    public SessionOptionsBackend(IAuth auth, ICommander commander, IServiceProvider services) : base(services)
    {
        _auth = auth;
        _commander = commander;
    }

    // [CommandHandler]
    public virtual async Task Upsert(ISessionOptionsBackend.UpsertCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) return;

        var sessionInfo = await _auth.GetSessionInfo(command.Session, cancellationToken).Require().ConfigureAwait(false);
        var options = sessionInfo.Options.Set(command.Option.Key, command.Option.Value);
        var setOptionsCommand = new SetSessionOptionsCommand(command.Session, options, sessionInfo.Version);
        await _commander.Call(setOptionsCommand, cancellationToken).ConfigureAwait(false);

        // Old implementation:
        /*
        if (Computed.IsInvalidating()) {
            _ = _auth.GetSessionInfo(command.Session, default);
            _ = _auth.GetOptions(command.Session, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbSession = await dbContext.Sessions
            .ForUpdate()
            .SingleAsync(s => s.Id == command.Session.Id.Value, cancellationToken)
            .ConfigureAwait(false);
        dbSession.Options = dbSession.Options
            .Set(command.Option.Key, command.Option.Value);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        */
    }
}
