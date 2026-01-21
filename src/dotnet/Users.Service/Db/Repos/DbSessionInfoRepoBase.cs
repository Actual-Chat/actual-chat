using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users.Db;

public class DbSessionInfoRepoBase<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbSessionInfo,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbUserId>(
    AuthBackend.Options settings,
    IServiceProvider services
    ) : DbServiceBase<UsersDbContext>(services), IDbSessionInfoRepo<TDbSessionInfo, TDbUserId>
    where TDbSessionInfo : DbSessionInfoBase<TDbUserId>, new()
    where TDbUserId : notnull
{
    protected AuthBackend.Options Settings { get; } = settings;
    protected IDbUserIdHandler<TDbUserId> UserIdHandler { get; init; }
        = services.GetRequiredService<IDbUserIdHandler<TDbUserId>>();
    protected IDbEntityResolver<string, TDbSessionInfo> SessionResolver { get; init; }
        = services.DbEntityResolver<string, TDbSessionInfo>();
    protected IDbEntityConverter<TDbSessionInfo, SessionInfo> SessionConverter { get; init; }
        = services.DbEntityConverter<TDbSessionInfo, SessionInfo>();

    public Type SessionInfoEntityType => typeof(TDbSessionInfo);

    // Write methods

    public virtual async Task<TDbSessionInfo> GetOrCreate(
        UsersDbContext dbContext, string sessionId, CancellationToken cancellationToken = default)
    {
        var dbSessionInfo = await Get(dbContext, sessionId, true, cancellationToken).ConfigureAwait(false);
        if (dbSessionInfo is null) {
            var session = new Session(sessionId);
            var sessionInfo = new SessionInfo(session, Clocks.SystemClock.Now);
            dbSessionInfo = dbContext.Add(
                new TDbSessionInfo() {
                    Id = sessionId,
                    CreatedAt = sessionInfo.CreatedAt,
                }).Entity;
            SessionConverter.UpdateEntity(sessionInfo, dbSessionInfo);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return dbSessionInfo;
    }

    public async Task<TDbSessionInfo> Upsert(
        UsersDbContext dbContext, string sessionId, SessionInfo sessionInfo, CancellationToken cancellationToken = default)
    {
        var dbSessionInfo = await dbContext.Set<TDbSessionInfo>().ForNoKeyUpdate()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
        var isDbSessionInfoFound = dbSessionInfo is not null;
        dbSessionInfo ??= new() {
            Id = sessionId,
            CreatedAt = sessionInfo.CreatedAt,
        };
        SessionConverter.UpdateEntity(sessionInfo, dbSessionInfo);
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

        return await dbContext.Set<TDbSessionInfo>()
            .Where(o => o.LastSeenAt < maxLastSeenAt)
            .OrderBy(o => o.LastSeenAt)
            .Take(maxCount)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // Read methods

    public async Task<TDbSessionInfo?> Get(string sessionId, CancellationToken cancellationToken = default)
        => await SessionResolver.Get(DbShard.Single, sessionId, cancellationToken).ConfigureAwait(false);

    public virtual async Task<TDbSessionInfo?> Get(
        UsersDbContext dbContext, string sessionId, bool forUpdate, CancellationToken cancellationToken = default)
    {
        var dbSessionInfos = forUpdate
            ? dbContext.Set<TDbSessionInfo>().ForNoKeyUpdate()
            : dbContext.Set<TDbSessionInfo>();
        return await dbSessionInfos
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task<TDbSessionInfo[]> ListByUser(
        UsersDbContext dbContext, TDbUserId userId, CancellationToken cancellationToken = default)
    {
        var qSessions =
            from s in dbContext.Set<TDbSessionInfo>().AsQueryable()
            where Equals(s.UserId, userId)
            orderby s.LastSeenAt descending
            select s;
        var sessions = (TDbSessionInfo[]) await qSessions.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return sessions;
    }
}
