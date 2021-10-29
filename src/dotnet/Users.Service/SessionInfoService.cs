using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class SessionInfoService : DbServiceBase<UsersDbContext>, ISessionInfoService
{
    public SessionInfoService(IServiceProvider services) : base(services) { }

    /// <inheritdoc />
    [CommandHandler, Internal]
    public virtual async Task Update(ISessionInfoService.UpsertData command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;
        var dbContext = await CreateCommandDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
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
