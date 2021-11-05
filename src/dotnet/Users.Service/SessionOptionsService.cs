using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class SessionOptionsService : DbServiceBase<UsersDbContext>, ISessionOptionsBackend
{
    private readonly IAuth _auth;

    public SessionOptionsService(IAuth auth, IServiceProvider services) : base(services)
        => _auth = auth;

    // Backend

    // [CommandHandler]
    public virtual async Task Update(ISessionOptionsBackend.UpdateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            _ = _auth.GetSessionInfo(command.Session, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbSession = await dbContext.Sessions
            .ForUpdate()
            .SingleAsync(s => s.Id == (string)command.Session.Id, cancellationToken)
            .ConfigureAwait(false);
        dbSession.Options = dbSession.Options
            .Set(command.Option.Key, command.Option.Value);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
