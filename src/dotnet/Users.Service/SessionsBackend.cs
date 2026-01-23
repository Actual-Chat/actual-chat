using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

public class SessionsBackend(
    SessionsBackend.Options settings,
    IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), ISessionsBackend
{
    public record Options
    {
        // The default should be less than 3 min - see PresenceService.Options
        public TimeSpan MinUpdatePresencePeriod { get; init; } = TimeSpan.FromMinutes(2.75);
    }

    protected Options Settings { get; } = settings;
    protected IDbEntityResolver<string, DbSessionInfo> SessionResolver { get; init; }
        = services.DbEntityResolver<string, DbSessionInfo>();

    // Commands

    // [CommandHandler]
    public virtual async Task OnSignOut(
        SessionsBackend_SignOut command, CancellationToken cancellationToken = default)
    {
        var session = command.Session.RequireValid();
        var force = command.Force;

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = Get(session, default);
            if (force)
                _ = IsSignOutForced(session, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbSessionInfo = await GetOrCreateDbSessionInfo(dbContext, session.Id, cancellationToken).ConfigureAwait(false);
        var sessionInfo = dbSessionInfo.ToModel(Log);
        if (sessionInfo.IsSignOutForced)
            return;

        // Capture user ID before sign-out for event emission
        var userId = UserId.ParseNullable(sessionInfo.UserId);

        sessionInfo = sessionInfo with {
            LastSeenAt = Clocks.SystemClock.Now,
            AuthenticatedIdentity = "",
            UserId = "",
            IsSignOutForced = force,
        };
        await UpsertDbSessionInfo(dbContext, session.Id, sessionInfo, cancellationToken).ConfigureAwait(false);

        // Emit UserSignedOutEvent if user was authenticated
        if (userId is not null)
            context.Operation.AddEvent(new UserSignedOutEvent(session.Id, force, userId));
    }

    // [CommandHandler]
    public virtual async Task<SessionInfo> OnUpsert(
        SessionsBackend_Upsert command, CancellationToken cancellationToken = default)
    {
        var (session, ipAddress, userAgent, options, userId, authenticatedIdentity) = command;
        session.RequireValid();

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invSessionInfo = context.Operation.Items.KeylessGet<SessionInfo>();
            if (invSessionInfo is null)
                return null!;

            _ = Get(session, default);
            return null!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbSessionInfo = await GetDbSessionInfo(dbContext, session.Id, true, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        var sessionInfo = dbSessionInfo?.ToModel(Log)
            ?? new SessionInfo(now) { SessionHash = session.Hash };
        sessionInfo = sessionInfo with {
            LastSeenAt = now,
            IPAddress = ipAddress.IsNullOrEmpty() ? sessionInfo.IPAddress : ipAddress,
            UserAgent = userAgent.IsNullOrEmpty() ? sessionInfo.UserAgent : userAgent,
            Options = options.SetMany(sessionInfo.Options),
        };
        // Update auth state if provided
        if (userId is not null)
            sessionInfo = sessionInfo with { UserId = userId };
        if (authenticatedIdentity is not null)
            sessionInfo = sessionInfo with { AuthenticatedIdentity = authenticatedIdentity };

        dbSessionInfo = await UpsertDbSessionInfo(dbContext, session.Id, sessionInfo, cancellationToken)
            .ConfigureAwait(false);
        sessionInfo = dbSessionInfo.ToModel(Log);
        context.Operation.Items.KeylessSet(sessionInfo);
        return sessionInfo!;
    }

    public virtual async Task UpdatePresence(
        Session session, CancellationToken cancellationToken = default)
    {
        var sessionInfo = await Get(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo is null)
            return;

        var delta = Clocks.SystemClock.Now - sessionInfo.LastSeenAt;
        if (delta < Settings.MinUpdatePresencePeriod)
            return; // We don't want to update this too frequently

        var upsertSessionCmd = new SessionsBackend_Upsert(session);
        await Commander.Call(upsertSessionCmd, cancellationToken).ConfigureAwait(false);
    }

    // Compute methods

    // [ComputeMethod]
    public virtual async Task<bool> IsSignOutForced(
        Session session, CancellationToken cancellationToken = default)
    {
        using var _ = Computed.BeginIsolation();
        var sessionInfo = await Get(session, cancellationToken).ConfigureAwait(false);
        return sessionInfo?.IsSignOutForced ?? false;
    }

    // [ComputeMethod]
    public virtual async Task<SessionInfo?> Get(Session session, CancellationToken cancellationToken = default)
    {
        session.RequireValid();
        var dbSessionInfo = await SessionResolver.Get(DbShard.Single, session.Id, cancellationToken).ConfigureAwait(false);
        return dbSessionInfo?.ToModel(Log);
    }

    // Private methods

    private async Task<DbSessionInfo> GetOrCreateDbSessionInfo(
        UsersDbContext dbContext, string sessionId, CancellationToken cancellationToken)
    {
        var dbSessionInfo = await GetDbSessionInfo(dbContext, sessionId, true, cancellationToken).ConfigureAwait(false);
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

    private async Task<DbSessionInfo> UpsertDbSessionInfo(
        UsersDbContext dbContext, string sessionId, SessionInfo sessionInfo, CancellationToken cancellationToken)
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

    private static async Task<DbSessionInfo?> GetDbSessionInfo(
        UsersDbContext dbContext, string sessionId, bool forUpdate, CancellationToken cancellationToken)
    {
        var dbSessionInfos = forUpdate
            ? dbContext.Set<DbSessionInfo>().ForNoKeyUpdate()
            : dbContext.Set<DbSessionInfo>();
        return await dbSessionInfos
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
    }

    }
