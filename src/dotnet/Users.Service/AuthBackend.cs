using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;

namespace ActualChat.Users;

public class AuthBackend(
    AuthBackend.Options settings,
    IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), IAuthBackend
{
    public record Options
    {
        // The default should be less than 3 min - see PresenceService.Options
        public TimeSpan MinUpdatePresencePeriod { get; init; } = TimeSpan.FromMinutes(2.75);
    }

    protected Options Settings { get; } = settings;
    protected IDbUserIdHandler<string> UserIdHandler { get; init; }
        = services.GetRequiredService<IDbUserIdHandler<string>>();
    protected IDbUserRepo<DbUser, string> Users { get; init; }
        = services.GetRequiredService<IDbUserRepo<DbUser, string>>();
    protected IDbEntityConverter<DbUser, User> UserConverter { get; init; }
        = services.DbEntityConverter<DbUser, User>();
    protected IDbSessionInfoRepo<DbSessionInfo, string> Sessions { get; init; }
        = services.GetRequiredService<IDbSessionInfoRepo<DbSessionInfo, string>>();
    protected IDbEntityConverter<DbSessionInfo, SessionInfo> SessionConverter { get; init; }
        = services.DbEntityConverter<DbSessionInfo, SessionInfo>();

    // Commands

    // [CommandHandler]
    public virtual async Task OnSignOut(
        Auth_SignOut command, CancellationToken cancellationToken = default)
    {
        var session = command.Session.RequireValid();
        var kickUserSessionHash = command.KickUserSessionHash;
        var kickAllUserSessions = command.KickAllUserSessions;
        var isKickCommand = kickAllUserSessions || !kickUserSessionHash.IsNullOrEmpty();
        var force = command.Force;

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            if (isKickCommand)
                return;

            _ = GetSessionInfo(session, default); // Must go first!
            _ = GetAuthInfo(session, default);
            if (force)
                _ = IsSignOutForced(session, default);
            var invSessionInfo = context.Operation.Items.KeylessGet<SessionInfo>();
            if (invSessionInfo is not null) {
                _ = GetUser(invSessionInfo.UserId, default);
                _ = GetUserSessions(session, default);
            }
            return;
        }

        // Let's handle special kinds of sign-out first, which only trigger "primary" sign-out version
        if (isKickCommand) {
            var user = await GetSessionUser(session, cancellationToken).ConfigureAwait(false);
            if (user is null)
                return;

            var userSessions = await GetUserSessionsInternal(user.Id, cancellationToken).ConfigureAwait(false);
            var signOutSessions = kickUserSessionHash.IsNullOrEmpty()
                ? userSessions
                : userSessions.Where(p => Equals(p.SessionInfo.SessionHash, kickUserSessionHash));
            foreach (var (sessionId, _) in signOutSessions) {
                var otherSessionSignOutCommand = new Auth_SignOut(new Session(sessionId), force);
                await Commander.Run(otherSessionSignOutCommand, isOutermost: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbSessionInfo = await Sessions.GetOrCreate(dbContext, session.Id, cancellationToken).ConfigureAwait(false);
        var sessionInfo = SessionConverter.ToModel(dbSessionInfo);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (sessionInfo is null || sessionInfo.IsSignOutForced)
            return;

        context.Operation.Items.KeylessSet(sessionInfo);
        sessionInfo = sessionInfo with {
            LastSeenAt = Clocks.SystemClock.Now,
            AuthenticatedIdentity = "",
            UserId = "",
            IsSignOutForced = force,
        };
        await Sessions.Upsert(dbContext, session.Id, sessionInfo, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnEditUser(Auth_EditUser command, CancellationToken cancellationToken = default)
    {
        var session = command.Session.RequireValid();

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invSessionInfo = context.Operation.Items.KeylessGet<SessionInfo>();
            if (invSessionInfo is not null)
                _ = GetUser(invSessionInfo.UserId, default);
            return;
        }

        var sessionInfo = await GetSessionInfo(session, cancellationToken)
            .Require(SessionInfo.MustBeAuthenticated)
            .ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbUserId = UserIdHandler.Parse(sessionInfo.UserId, false);
        var dbUser = await Users.Get(dbContext, dbUserId, true, cancellationToken).ConfigureAwait(false);
        if (dbUser is null)
            throw StandardError.NotFound<User>("User not found.");

        await Users.Edit(dbContext, dbUser, command, cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(sessionInfo);
    }

    public virtual async Task UpdatePresence(
        Session session, CancellationToken cancellationToken = default)
    {
        var sessionInfo = await GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo is null)
            return;

        var delta = Clocks.SystemClock.Now - sessionInfo.LastSeenAt;
        if (delta < Settings.MinUpdatePresencePeriod)
            return; // We don't want to update this too frequently

        var command = new AuthBackend_SetupSession(session);
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSignIn(
        AuthBackend_SignIn command, CancellationToken cancellationToken = default)
    {
        var (session, user, authenticatedIdentity) = (command.Session, command.User, command.AuthenticatedIdentity);
        session.RequireValid();

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = GetSessionInfo(session, default); // Must go first!
            _ = GetAuthInfo(session, default);
            var invSessionInfo = context.Operation.Items.KeylessGet<SessionInfo>();
            if (invSessionInfo is not null) {
                _ = GetUser(invSessionInfo.UserId, default);
                _ = GetUserSessions(session, default);
            }
            return;
        }

        if (!user.Identities.ContainsKey(authenticatedIdentity))
#pragma warning disable MA0015
            throw new ArgumentOutOfRangeException(
                $"{nameof(command)}.{nameof(AuthBackend_SignIn.AuthenticatedIdentity)}");
#pragma warning restore MA0015

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbSessionInfo = await Sessions.GetOrCreate(dbContext, session.Id, cancellationToken).ConfigureAwait(false);
        var sessionInfo = SessionConverter.ToModel(dbSessionInfo);
        if (sessionInfo!.IsSignOutForced)
            throw StandardError.Unauthorized("Session unavailable.");

        var isNewUser = false;
        var dbUser = await Users
            .GetByUserIdentity(dbContext, authenticatedIdentity, true, cancellationToken)
            .ConfigureAwait(false);
        if (dbUser is null) {
            (dbUser, isNewUser) = await Users
                .GetOrCreateOnSignIn(dbContext, user, cancellationToken)
                .ConfigureAwait(false);
            if (isNewUser == false) {
                UserConverter.UpdateEntity(user, dbUser);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        else {
            user = user with {
                Id = UserIdHandler.Format(dbUser.Id)
            };
            UserConverter.UpdateEntity(user, dbUser);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        sessionInfo = sessionInfo with {
            LastSeenAt = Clocks.SystemClock.Now,
            AuthenticatedIdentity = authenticatedIdentity,
            UserId = UserIdHandler.Format(dbUser.Id)
        };
        await Sessions.Upsert(dbContext, session.Id, sessionInfo, cancellationToken).ConfigureAwait(false);

        context.Operation.Items.KeylessSet(sessionInfo);
        context.Operation.Items.KeylessSet(isNewUser);
    }

    // [CommandHandler]
    public virtual async Task<SessionInfo> OnSetupSession(
        AuthBackend_SetupSession command, CancellationToken cancellationToken = default)
    {
        var (session, ipAddress, userAgent, options) = command;
        session.RequireValid();

        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invSessionInfo = context.Operation.Items.KeylessGet<SessionInfo>();
            if (invSessionInfo is null)
                return null!;

            _ = GetSessionInfo(session, default); // Must go first!
            var invIsNew = context.Operation.Items.KeylessGet<bool>();
            if (invIsNew)
                _ = GetAuthInfo(session, default);
            if (invSessionInfo.IsAuthenticated())
                _ = GetUserSessions(session, default);
            return null!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbSessionInfo = await Sessions.Get(dbContext, session.Id, true, cancellationToken).ConfigureAwait(false);
        var isNew = dbSessionInfo is null;
        var now = Clocks.SystemClock.Now;
        var sessionInfo = SessionConverter.ToModel(dbSessionInfo)
            ?? SessionConverter.NewModel() with { SessionHash = session.Hash };
        sessionInfo = sessionInfo with {
            LastSeenAt = now,
            IPAddress = ipAddress.IsNullOrEmpty() ? sessionInfo.IPAddress : ipAddress,
            UserAgent = userAgent.IsNullOrEmpty() ? sessionInfo.UserAgent : userAgent,
            Options = options.SetMany(sessionInfo.Options),
        };
        dbSessionInfo = await Sessions
            .Upsert(dbContext, session.Id, sessionInfo, cancellationToken)
            .ConfigureAwait(false);
        sessionInfo = SessionConverter.ToModel(dbSessionInfo);
        context.Operation.Items.KeylessSet(sessionInfo); // invSessionInfo
        context.Operation.Items.KeylessSet(isNew); // invIsNew
        return sessionInfo!;
    }

    // [CommandHandler]
    public virtual async Task OnSetOptions(
        AuthBackend_SetSessionOptions command, CancellationToken cancellationToken = default)
    {
        var (session, options, expectedVersion) = command;
        session.RequireValid();

        if (Invalidation.IsActive) {
            _ = GetSessionInfo(session, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);

        var dbSessionInfo = await Sessions.Get(dbContext, session.Id, true, cancellationToken).ConfigureAwait(false);
        var sessionInfo = SessionConverter.ToModel(dbSessionInfo);
        if (sessionInfo is null)
            throw new KeyNotFoundException();
        VersionChecker.RequireExpected(sessionInfo.Version, expectedVersion);
        sessionInfo = sessionInfo with {
            LastSeenAt = Clocks.SystemClock.Now,
            Options = options,
        };
        await Sessions.Upsert(dbContext, session.Id, sessionInfo, cancellationToken).ConfigureAwait(false);
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
        var sessionInfo = await GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        return sessionInfo?.ToAuthInfo();
    }

    // [ComputeMethod]
    public virtual async Task<SessionInfo?> GetSessionInfo(Session session, CancellationToken cancellationToken = default)
    {
        session.RequireValid();
        var dbSessionInfo = await Sessions.Get(session.Id, cancellationToken).ConfigureAwait(false);
        return dbSessionInfo is null ? null : SessionConverter.ToModel(dbSessionInfo);
    }

    // [ComputeMethod]
    public virtual async Task<User?> GetUser(
        string userId, CancellationToken cancellationToken = default)
    {
        if (!UserIdHandler.TryParse(userId, false, out var dbUserId))
            return null;

        var dbUser = await Users.Get(dbUserId, cancellationToken).ConfigureAwait(false);
        return UserConverter.ToModel(dbUser);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<SessionInfo>> GetUserSessions(
        Session session, CancellationToken cancellationToken = default)
    {
        var user = await GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return ImmutableArray<SessionInfo>.Empty;

        var sessions = await GetUserSessionsInternal(user.Id, cancellationToken).ConfigureAwait(false);
        return [..sessions.Select(p => p.SessionInfo)];
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<User?> GetSessionUser(
        Session session, CancellationToken cancellationToken = default)
    {
        var authInfo = await GetAuthInfo(session, cancellationToken).ConfigureAwait(false);
        if (!(authInfo?.IsAuthenticated() ?? false))
            return null;

        var user = await GetUser(authInfo.UserId, cancellationToken).ConfigureAwait(false);
        return user;
    }

    [ComputeMethod]
    protected virtual async Task<ImmutableArray<(string Id, SessionInfo SessionInfo)>> GetUserSessionsInternal(
        string userId, CancellationToken cancellationToken = default)
    {
        if (!UserIdHandler.TryParse(userId, false, out var dbUserId))
            return ImmutableArray<(string Id, SessionInfo SessionInfo)>.Empty;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var _2 = tx.ConfigureAwait(false);

        var dbSessions = await Sessions.ListByUser(dbContext, dbUserId, cancellationToken).ConfigureAwait(false);
        var sessions = dbSessions
            .Select(x => (x.Id, SessionConverter.ToModel(x)!))
            .ToImmutableArray();
        return sessions;
    }
}
