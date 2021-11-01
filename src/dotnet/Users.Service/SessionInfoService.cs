using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class SessionInfoService : DbServiceBase<UsersDbContext>, ISessionInfoService
{
    private readonly IAuthService _auth;

    public SessionInfoService(IAuthService auth, IServiceProvider services) : base(services)
    {
        _auth = auth;
    }

    /// <inheritdoc />
    [CommandHandler, Internal]
    public virtual async Task Update(ISessionInfoService.UpsertCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            _ = _auth.GetSessionInfo(command.Session, default);
            return;
        }
        var dbContext = await CreateCommandDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var dbSession = await dbContext.Sessions.FirstOrDefaultAsync(x => x.Id == (string)command.Session.Id, cancellationToken)
            .ConfigureAwait(false);
        if (dbSession == null)
            throw new ArgumentOutOfRangeException(nameof(command), "Session is not found");

        dbSession.Options = new ImmutableOptionSet(
            dbSession.Options.Items.SetItem(command.KeyValue.Key, command.KeyValue.Value)
        );

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
