using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class SessionOptionsBackend : DbServiceBase<UsersDbContext>, ISessionOptionsBackend
{
    private readonly IAuth _auth;
    private readonly IAuthBackend _authBackend;

    public SessionOptionsBackend(IAuth auth, IAuthBackend authBackend, IServiceProvider services) : base(services)
    {
        _auth = auth;
        _authBackend = authBackend;
    }

    // [CommandHandler]
    public virtual async Task Upsert(ISessionOptionsBackend.UpsertCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) return;

        var sessionInfo = await _auth.GetSessionInfo(command.Session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo == null)
            throw new KeyNotFoundException();

        var options = sessionInfo.Options.Set(command.Option.Key, command.Option.Value);
        await _authBackend.SetOptions(new(command.Session, options, sessionInfo.Version), cancellationToken)
            .ConfigureAwait(false);

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
