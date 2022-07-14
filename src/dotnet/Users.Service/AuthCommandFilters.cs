using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Internal;

namespace ActualChat.Users;

public class AuthCommandFilters : DbServiceBase<UsersDbContext>
{
    protected IAuth Auth { get; }
    protected IAuthBackend AuthBackend { get; }
    protected IAccountsBackend AccountsBackend { get; }
    protected UserNamer UserNamer { get; }
    protected IUserPresences UserPresences { get; }
    protected IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; }

    public AuthCommandFilters(IServiceProvider services)
        : base(services)
    {
        Auth = services.GetRequiredService<IAuth>();
        AuthBackend = services.GetRequiredService<IAuthBackend>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        UserNamer = services.GetRequiredService<UserNamer>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        DbUsers = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();
    }

    /// <summary> The filter which clears Sessions.OptionsJson field in the database </summary>
    [CommandHandler(IsFilter = true, Priority = 1)]
    public virtual async Task OnSignOut(SignOutCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var session = command.Session;

        // Invoke command handlers with lower priority
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        if (Computed.IsInvalidating()) {
            _ = Auth.GetOptions(session, default);
            return;
        }

        await ResetSessionOptions(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary> Takes care of invalidation of IsOnlineAsync once user signs in. </summary>
    [CommandHandler(IsFilter = true, Priority = 1)]
    public virtual async Task OnSignInMarkOnline(SignInCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var session = command.Session;

        // Invoke command handlers with lower priority
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        if (Computed.IsInvalidating()) {
            _ = Auth.GetOptions(session, default);
            var invUserId = context.Operation().Items.Get<string>();
            if (!invUserId.IsNullOrEmpty())
                _ = UserPresences.Get(invUserId, default);
            return;
        }

        await ResetSessionOptions(command.Session, cancellationToken).ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var sessionInfo = context.Operation().Items.Get<SessionInfo>(); // Set by default command handler
        if (sessionInfo == null)
            throw Errors.InternalError("No SessionInfo in operation's items.");
        var userId = sessionInfo.UserId;
        var dbUser = await DbUsers.Get(dbContext, userId, true, cancellationToken).ConfigureAwait(false);
        if (dbUser == null)
            return; // Should never happen, but if it somehow does, there is no extra to do in this case

        // Let's try to fix auto-generated user name here
        var newName = UserNamer.NormalizeName(dbUser.Name);
        if (!OrdinalEquals(newName, dbUser.Name)) {
            dbUser.Name = newName;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        context.Operation().Items.Set(dbUser.Id);
        await MarkOnline(userId, cancellationToken).ConfigureAwait(false);
    }

    [CommandHandler(IsFilter = true, Priority = 1)]
    protected virtual async Task OnEditUser(EditUserCommand command, CancellationToken cancellationToken)
    {
        // This command filter validates user name on edit

        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
            return;
        }
        if (command.Name != null) {
            var error = UserNamer.ValidateName(command.Name);
            if (error != null)
                throw error;
        }
        // Invoke command handler(s) with lower priority
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
    }

    // Updates online presence state
    [CommandHandler(IsFilter = true, Priority = 1)]
    public virtual async Task SetupSession(
        SetupSessionCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        var sessionInfo = context.Operation().Items.Get<SessionInfo>();
        if (sessionInfo?.IsAuthenticated() != true)
            return;

        var userId = sessionInfo.UserId;
        await MarkOnline(userId, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task MarkOnline(string userId, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            var c = Computed.GetExisting(() => UserPresences.Get(userId, default));
            if (c?.IsConsistent() != true)
                return;
            if (c.IsValue(out var v) && v is Presence.Online or Presence.Recording)
                return;
            // We invalidate only when there is a cached value, and it is
            // either false or an error, because the only change that may happen
            // due to sign-in is that this value becomes true.
            _ = UserPresences.Get(userId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var userState = await dbContext.UserPresences
            .ForUpdate()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (userState == null) {
            userState = new DbUserPresence() { UserId = userId };
            dbContext.Add(userState);
        }
        userState.OnlineCheckInAt = Clocks.SystemClock.Now;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ResetSessionOptions(Session session, CancellationToken cancellationToken)
    {
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbSession = await dbContext.Sessions
            .ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == session.Id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbSession != null) {
            dbSession.Options = new ImmutableOptionSet();
            dbSession.Version = VersionGenerator.NextVersion(dbSession.Version);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

}
