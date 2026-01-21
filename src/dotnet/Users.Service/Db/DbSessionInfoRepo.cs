using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.Db;

public class DbSessionInfoRepo(IServiceProvider services) : DbServiceBase<UsersDbContext>(services)
{
    protected IDbEntityResolver<string, DbSessionInfo> SessionResolver { get; init; }
        = services.DbEntityResolver<string, DbSessionInfo>();

    // Write methods

    public virtual async Task<DbSessionInfo> GetOrCreate(
        UsersDbContext dbContext, string sessionId, CancellationToken cancellationToken = default)
    {
        var dbSessionInfo = await Get(dbContext, sessionId, true, cancellationToken).ConfigureAwait(false);
        if (dbSessionInfo is null) {
            var session = new Session(sessionId);
            var sessionInfo = new SessionInfo(session, Clocks.SystemClock.Now);
            dbSessionInfo = dbContext.Add(
                new DbSessionInfo() {
                    Id = sessionId,
                    CreatedAt = sessionInfo.CreatedAt,
                }).Entity;
            dbSessionInfo.UpdateFrom(sessionInfo, VersionGenerator);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return dbSessionInfo;
    }

    public async Task<DbSessionInfo> Upsert(
        UsersDbContext dbContext, string sessionId, SessionInfo sessionInfo, CancellationToken cancellationToken = default)
    {
        var dbSessionInfo = await dbContext.Set<DbSessionInfo>().ForNoKeyUpdate()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
        var isDbSessionInfoFound = dbSessionInfo is not null;
        dbSessionInfo ??= new() {
            Id = sessionId,
            CreatedAt = sessionInfo.CreatedAt,
        };
        dbSessionInfo.UpdateFrom(sessionInfo, VersionGenerator);
        if (isDbSessionInfoFound)
            dbContext.Update(dbSessionInfo);
        else
            dbContext.Add(dbSessionInfo);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbSessionInfo;
    }

    public virtual async Task<int> Trim(
        DateTime maxLastSeenAt, int maxCount, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(true, cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(false);

        return await dbContext.Set<DbSessionInfo>()
            .Where(o => o.LastSeenAt < maxLastSeenAt)
            .OrderBy(o => o.LastSeenAt)
            .Take(maxCount)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // Read methods

    public async Task<DbSessionInfo?> Get(string sessionId, CancellationToken cancellationToken = default)
        => await SessionResolver.Get(DbShard.Single, sessionId, cancellationToken).ConfigureAwait(false);

    public virtual async Task<DbSessionInfo?> Get(
        UsersDbContext dbContext, string sessionId, bool forUpdate, CancellationToken cancellationToken = default)
    {
        var dbSessionInfos = forUpdate
            ? dbContext.Set<DbSessionInfo>().ForNoKeyUpdate()
            : dbContext.Set<DbSessionInfo>();
        return await dbSessionInfos
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task<DbSessionInfo[]> ListByUser(
        UsersDbContext dbContext, string userId, CancellationToken cancellationToken = default)
    {
        var qSessions =
            from s in dbContext.Set<DbSessionInfo>().AsQueryable()
            where Equals(s.UserId, userId)
            orderby s.LastSeenAt descending
            select s;
        var sessions = (DbSessionInfo[]) await qSessions.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return sessions;
    }
}
