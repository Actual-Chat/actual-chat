using ActualChat.Users.Db;
using ActualChat.Users.Events;
using ActualLab.Fusion.Authentication.Services;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class AuthBackendCommandFilters(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), ICommandService
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

        using var _activity = AppDiagnostics.AppTrace.StartActivity("AuthBackendCommandFilters:OnSignIn");
        var context = CommandContext.GetCurrent();
        using (var _InvokeRemainingHandlers = AppDiagnostics.AppTrace.StartActivity("OnSignIn:InvokeRemainingHandlers")) {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
        }
        SessionInfo? sessionInfo;
        using (var _GetSessionInfo = AppDiagnostics.AppTrace.StartActivity("OnSignIn:GetSessionInfo")) {
            sessionInfo = context.Operation.Items.Get<SessionInfo>(); // Set by default command handler
            if (sessionInfo == null)
                throw StandardError.Internal("No SessionInfo in operation's items.");
        }
        UserId userId;
        using (var _Invalidation = AppDiagnostics.AppTrace.StartActivity("OnSignIn:Invalidation")) {
            userId = new UserId(sessionInfo.UserId);
            if (Invalidation.IsActive) {
                if (context.Operation.Items.Get<UserNameChangedTag>() != null)
                    _ = AuthBackend.GetUser(default, userId, default);
                return;
            }
        }

        UsersDbContext dbContext;
        using (var _CreateCommandDbContext = AppDiagnostics.AppTrace.StartActivity("OnSignIn:CreateCommandDbContext")) {
            dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);
        }
        DbUser dbUser;
        using (var _GetDbUser = AppDiagnostics.AppTrace.StartActivity("OnSignIn:GetDbUser")) {
            dbUser = await DbUsers.Get(dbContext, userId, true, cancellationToken).ConfigureAwait(false);
            if (dbUser == null)
                return; // Should never happen, but if it somehow does, there is no extra to do in this case
        }

        // Let's try to fix auto-generated user name here
        var newName = UserNamer.NormalizeName(dbUser.Name);
        if (!OrdinalEquals(newName, dbUser.Name)) {
            context.Operation.Items.Set(new UserNameChangedTag());
            dbUser.Name = newName;
        }
        using (var _SaveChangesAsync = AppDiagnostics.AppTrace.StartActivity("OnSignIn:SaveChangesAsync")) {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // MigrateGuestKeys is disabled for now, coz it causes more problems than solves
        // context.Operation.AddEvent(new ServerKvas_MigrateGuestKeys(command.Session));

        // Raise events
        var isNewUser = context.Operation.Items.GetOrDefault<bool>(); // Set by default command handler
        if (isNewUser)
            context.Operation.AddEvent(new NewUserEvent(userId));
    }

    // Nested types

    private record UserNameChangedTag;
}
