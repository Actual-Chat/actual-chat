using ActualChat.Commands;
using ActualChat.Kvas;
using ActualChat.Users.Db;
using ActualChat.Users.Events;
using ActualLab.Fusion.Authentication.Services;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Internal;

namespace ActualChat.Users;

public class AuthCommandFilters(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), ICommandService
{
    protected IAuthBackend AuthBackend { get; } = services.GetRequiredService<IAuthBackend>();
    protected IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    protected UserNamer UserNamer { get; } = services.GetRequiredService<UserNamer>();
    protected IUserPresences UserPresences { get; } = services.GetRequiredService<IUserPresences>();
    protected IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; } = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();

    [CommandFilter(Priority = 1)]
    public virtual async Task OnSignIn(AuthBackend_SignIn command, CancellationToken cancellationToken)
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

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    [CommandFilter(Priority = FusionEntityFrameworkCommandHandlerPriority.DbOperationScopeProvider + 1)]
    public virtual async Task OnSignedIn(AuthBackend_SignIn command, CancellationToken cancellationToken)
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

        // Follow-up actions
        var userId = new UserId(sessionInfo.UserId);

        // MigrateGuestKeys is disabled for now, coz it causes more problems than solves
        // new ServerKvas_MigrateGuestKeys(command.Session)
        //     .EnqueueOnCompletion();

        // Raise events
        var isNewUser = context.Operation().Items.GetOrDefault<bool>(); // Set by default command handler
        if (isNewUser)
            new NewUserEvent(userId)
                .EnqueueOnCompletion();
    }

    [CommandFilter(Priority = 1)]
    protected virtual async Task OnEditUser(Auth_EditUser command, CancellationToken cancellationToken)
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

    // Nested types

    private record UserNameChangedTag;
}
