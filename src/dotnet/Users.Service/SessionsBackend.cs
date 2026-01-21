using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

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
    protected DbSessionInfoRepo Sessions { get; init; }
        = services.GetRequiredService<DbSessionInfoRepo>();

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
            _ = GetAuthInfo(session, default);
            if (force)
                _ = IsSignOutForced(session, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbSessionInfo = await Sessions.GetOrCreate(dbContext, session.Id, cancellationToken).ConfigureAwait(false);
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
        await Sessions.Upsert(dbContext, session.Id, sessionInfo, cancellationToken).ConfigureAwait(false);

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
            // Invalidate GetAuthInfo when session is new or auth state changed
            var meta = context.Operation.Items.KeylessGet<SessionUpsertMeta>();
            var (invIsNew, invAuthChanged) = meta != null ? (meta.IsNew, meta.AuthChanged) : (false, false);
            if (invIsNew || invAuthChanged)
                _ = GetAuthInfo(session, default);
            return null!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbSessionInfo = await Sessions.Get(dbContext, session.Id, true, cancellationToken).ConfigureAwait(false);
        var isNew = dbSessionInfo is null;
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
        var authChanged = false;
        if (userId is not null || authenticatedIdentity is not null) {
            if (userId is not null)
                sessionInfo = sessionInfo with { UserId = userId };
            if (authenticatedIdentity is not null)
                sessionInfo = sessionInfo with { AuthenticatedIdentity = authenticatedIdentity };
            authChanged = true;
        }

        dbSessionInfo = await Sessions
            .Upsert(dbContext, session.Id, sessionInfo, cancellationToken)
            .ConfigureAwait(false);
        sessionInfo = dbSessionInfo.ToModel(Log);
        context.Operation.Items.KeylessSet(sessionInfo);
        context.Operation.Items.KeylessSet(new SessionUpsertMeta(isNew, authChanged));
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
        var sessionInfo = await GetAuthInfo(session, cancellationToken).ConfigureAwait(false);
        return sessionInfo?.IsSignOutForced ?? false;
    }

    // [ComputeMethod]
    public virtual async Task<SessionAuthInfo?> GetAuthInfo(
        Session session, CancellationToken cancellationToken = default)
    {
        session.RequireValid();
        using var _ = Computed.BeginIsolation();
        var sessionInfo = await Get(session, cancellationToken).ConfigureAwait(false);
        return sessionInfo?.ToAuthInfo();
    }

    // [ComputeMethod]
    public virtual async Task<SessionInfo?> Get(Session session, CancellationToken cancellationToken = default)
    {
        session.RequireValid();
        var dbSessionInfo = await Sessions.Get(session.Id, cancellationToken).ConfigureAwait(false);
        return dbSessionInfo?.ToModel(Log);
    }

    // Nested types

    // Must be Newtonsoft.Json serializable - stored in Operation.Items
    private sealed record SessionUpsertMeta(bool IsNew, bool AuthChanged);
}
