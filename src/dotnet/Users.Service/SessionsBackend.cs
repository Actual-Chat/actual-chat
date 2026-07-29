using ActualChat.Db;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users;

/// <summary>
/// Backend service implementation for session management and authentication state.
/// </summary>
public class SessionsBackend(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), ISessionsBackend
{
    private static readonly TimeSpan MinLastSeenAtUpdatePeriod
        = (Constants.Session.LastSeenAtUpdatePeriod - TimeSpan.FromMinutes(1)).Positive();

    private IAccountsBackend AccountsBackend
        => field ??= Services.GetRequiredService<IAccountsBackend>();
    private IDbEntityResolver<string, DbSession> SessionResolver
        => field ??= Services.DbEntityResolver<string, DbSession>();

    // Commands

    // [CommandHandler]
    public virtual async Task<SessionInfoFull> OnUpsert(
        SessionsBackend_Upsert command, CancellationToken cancellationToken = default)
    {
        var session = command.Session;
        session.RequireValid();

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = Get(session, default);
            if (context.Operation.Items.Get<UserId?>("OldUserId") is { } invOldUserId)
                _ = AccountsBackend.ListSessions(invOldUserId, default);
            if (context.Operation.Items.Get<UserId?>("NewUserId") is { } invNewUserId)
                _ = AccountsBackend.ListSessions(invNewUserId, default);
            return null!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        // Acquire advisory lock first to prevent deadlocks
        await dbContext.Sessions.Lock(session.Id, cancellationToken).ConfigureAwait(false);

        var dbSession = await GetDbSession(dbContext, session.Id, true, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        var sessionInfo = dbSession?.ToModel()
            ?? new SessionInfoFull(session) {
                CreatedAt = now,
                ExpiresAt = now + (session.Kind is SessionKind.ApiKey
                    ? CoreConstants.Session.ApiKeyExpirationTime
                    : CoreConstants.Session.SessionExpirationTime),
            };
        var oldUserId = sessionInfo.UserId;
        sessionInfo = sessionInfo with {
            LastSeenAt = now,
            ExpiresAt = command.ExpiresAt ?? sessionInfo.ExpiresAt,
            IPAddress = command.IPAddress ?? sessionInfo.IPAddress,
            Description = command.Description ?? sessionInfo.Description,
            AuthenticatedIdentity = command.AuthenticatedIdentity ?? sessionInfo.AuthenticatedIdentity,
            UserId = command.UserId.IsSome(out var vUserId) ? vUserId : sessionInfo.UserId,
        };
        var newUserId = sessionInfo.UserId;
        dbSession = await UpsertDbSession(dbContext, session.Id, sessionInfo, cancellationToken).ConfigureAwait(false);

        // Update DbUserSession mappings
        if (oldUserId != newUserId) {
            // Remove old mapping
            if (oldUserId is not null) {
                await dbContext.UserSessions
                    .Where(x => x.UserId == oldUserId.Value && x.SessionId == session.Id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                context.Operation.Items.Set("OldUserId", oldUserId);
            }
            // Add new mapping
            if (newUserId is not null) {
                dbContext.UserSessions.Add(new DbUserSession {
                    UserId = newUserId.Value,
                    SessionId = session.Id,
                });
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                context.Operation.Items.Set("NewUserId", newUserId);
            }
        }

        sessionInfo = dbSession.ToModel();
        return sessionInfo;
    }

    public virtual async Task UpdateLastSeenAt(
        Session session, string? description, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var sessionInfo = await Get(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo is null)
            return;

        var delta = Clocks.SystemClock.Now - sessionInfo.LastSeenAt;
        if (delta < MinLastSeenAtUpdatePeriod)
            return; // We don't want to update this too frequently

        var upsertSessionCmd = new SessionsBackend_Upsert(session) {
            Description = description,
            IPAddress = ipAddress,
        };
        if (session.Kind is SessionKind.Session) // Rolling expiration for regular sessions
            upsertSessionCmd = upsertSessionCmd with {
                ExpiresAt = Clocks.SystemClock.Now + CoreConstants.Session.SessionExpirationTime,
            };
        await Commander.Call(upsertSessionCmd, cancellationToken).ConfigureAwait(false);
    }

    // Compute methods

    // [ComputeMethod]
    public virtual async Task<SessionInfoFull?> Get(Session session, CancellationToken cancellationToken = default)
    {
        // A normal client state, not an exception: throwing cached an error nothing could invalidate.
        if (!session.IsValid())
            return null;

        var dbSession = await SessionResolver.Get(DbShard.Single, session.Id, cancellationToken).ConfigureAwait(false);
        return dbSession?.ToModel();
    }

    // Private methods

    private async Task<DbSession> UpsertDbSession(
        UsersDbContext dbContext, string sessionId, SessionInfoFull sessionInfo, CancellationToken cancellationToken)
    {
        var dbSession = await dbContext.Sessions.ForNoKeyUpdate()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
        var isFound = dbSession is not null;
        dbSession ??= new() {
            Id = sessionId,
            CreatedAt = sessionInfo.CreatedAt,
            ExpiresAt = sessionInfo.ExpiresAt.ToDateTime(),
        };
        dbSession.UpdateFrom(sessionInfo, VersionGenerator);
        if (isFound)
            dbContext.Update(dbSession);
        else
            dbContext.Add(dbSession);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbSession;
    }

    private static async Task<DbSession?> GetDbSession(
        UsersDbContext dbContext, string sessionId, bool forUpdate, CancellationToken cancellationToken)
    {
        var dbSessions = forUpdate
            ? dbContext.Sessions.ForNoKeyUpdate()
            : dbContext.Sessions;
        return await dbSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
    }
}
