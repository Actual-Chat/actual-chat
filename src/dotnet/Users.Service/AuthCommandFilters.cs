using ActualChat.Commands;
using ActualChat.Kvas;
using ActualChat.Users.Db;
using ActualChat.Users.Events;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Fusion.EntityFramework.Internal;

namespace ActualChat.Users;

public class AuthCommandFilters : DbServiceBase<UsersDbContext>
{
    protected IAuthBackend AuthBackend { get; }
    protected IAccountsBackend AccountsBackend { get; }
    protected UserNamer UserNamer { get; }
    protected IUserPresences UserPresences { get; }
    protected IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; }

    public AuthCommandFilters(IServiceProvider services)
        : base(services)
    {
        AuthBackend = services.GetRequiredService<IAuthBackend>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        UserNamer = services.GetRequiredService<UserNamer>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        DbUsers = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();
    }

    [CommandHandler(IsFilter = true, Priority = 1)]
    public virtual async Task OnSignIn(SignInCommand command, CancellationToken cancellationToken)
    {
        // This command filter takes the following actions on sign-in:
        // - Normalizes user name & invalidates AuthBackend.GetUser if it was changed
        // - Updates UserPresence.Get & invalidates it if it's not computed or offline

        var context = CommandContext.GetCurrent();
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        var sessionInfo = context.Operation().Items.Get<SessionInfo>(); // Set by default command handler
        if (sessionInfo == null)
            throw StandardError.Internal("No SessionInfo in operation's items.");
        var userId = new UserId(sessionInfo.UserId);

        if (Computed.IsInvalidating()) {
            InvalidatePresenceIfOffline(userId);
            if (context.Operation().Items.Get<UserNameChangedTag>() != null)
                _ = AuthBackend.GetUser(default, userId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUser = await DbUsers.Get(dbContext, userId, true, cancellationToken).ConfigureAwait(false);
        if (dbUser == null)
            return; // Should never happen, but if it somehow does, there is no extra to do in this case

        // Let's try to fix auto-generated user name here
        var newName = UserNamer.NormalizeName(dbUser.Name);
        if (!OrdinalEquals(newName, dbUser.Name)) {
            context.Operation().Items.Set(new UserNameChangedTag());
            dbUser.Name = newName;
        }

        await UpdatePresence(dbContext, userId, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    [CommandHandler(IsFilter = true, Priority = FusionEntityFrameworkCommandHandlerPriority.DbOperationScopeProvider + 1)]
    public virtual async Task OnSignedIn(SignInCommand command, CancellationToken cancellationToken)
    {
        // This command filter takes the following actions on sign-in:
        // - moves session keys to user keys in IServerKvas
        // - publishes NewUserEvent when user was created within sign-in.

        var context = CommandContext.GetCurrent();
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        if (Computed.IsInvalidating())
            return;

        var sessionInfo = context.Operation().Items.Get<SessionInfo>(); // Set by default command handler
        if (sessionInfo == null)
            throw StandardError.Internal("No SessionInfo in operation's items.");
        var userId = new UserId(sessionInfo.UserId);

        new IServerKvas.MigrateGuestKeysCommand(command.Session)
            .EnqueueOnCompletion(Queues.Users.ShardBy(userId));

        var isNewUser = context.Operation().Items.GetOrDefault<bool>(); // Set by default command handler
        if (isNewUser)
            new NewUserEvent(userId)
                .EnqueueOnCompletion(Queues.Users.ShardBy(userId));
    }

    [CommandHandler(IsFilter = true, Priority = 1)]
    protected virtual async Task OnEditUser(EditUserCommand command, CancellationToken cancellationToken)
    {
        // This command filter takes the following actions on user name edit:
        // - Validates user name

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

        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
    }

    [CommandHandler(IsFilter = true, Priority = 1)]
    public virtual async Task OnSetupSession(SetupSessionCommand command, CancellationToken cancellationToken)
    {
        // This command filter takes the following actions when session gets "touched" or setup:
        // - Updates UserPresence.Get & invalidates it if it's not computed or offline

        var context = CommandContext.GetCurrent();

        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        var sessionInfo = context.Operation().Items.Get<SessionInfo>(); // Set by default command handler
        var userId = new UserId(sessionInfo?.UserId, ParseOptions.OrDefault);
        if (userId.IsEmpty)
            return;

        if (Computed.IsInvalidating()) {
            InvalidatePresenceIfOffline(userId);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await UpdatePresence(dbContext, userId, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task UpdatePresence(UsersDbContext dbContext, UserId userId, CancellationToken cancellationToken)
    {
        var dbUserPresence = await dbContext.UserPresences
            .ForUpdate()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (dbUserPresence == null) {
            dbUserPresence = new DbUserPresence() { UserId = userId };
            dbContext.Add(dbUserPresence);
        }
        dbUserPresence.OnlineCheckInAt = Clocks.SystemClock.Now;
    }

    private void InvalidatePresenceIfOffline(UserId userId)
    {
        var c = Computed.GetExisting(() => UserPresences.Get(userId, default));
        if (c == null || c.IsInvalidated())
            return; // No computed to invalidate
        if (c.IsConsistent() && c.IsValue(out var v) && v is not Presence.Offline)
            return; // Consistent + already in desirable (non-Offline) state

        _ = UserPresences.Get(userId, default);
    }

    // Nested types

    private record UserNameChangedTag;
}
