using ActualChat.Users.Db;
using ActualChat.Users.Events;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class AuthCommandFilters(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), ICommandService
{
    protected UserNamer UserNamer { get; } = services.GetRequiredService<UserNamer>();
    protected IAuth Auth { get; } = services.GetRequiredService<IAuth>();

    [CommandFilter(Priority = 1)]
    protected virtual async Task OnEditUser(Auth_EditUser command, CancellationToken cancellationToken)
    {
        // This command filter takes the following actions on user name edit:
        // - Validates user name

        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
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

    [CommandFilter(Priority = 1)]
    public virtual async Task OnSignOut(
        Auth_SignOut command,
        CancellationToken cancellationToken)
    {
        // This command filter emits UserSignedOutEvent:
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
            return;
        }

        var session = command.Session;
        var authInfo = await Auth.GetAuthInfo(session, cancellationToken).ConfigureAwait(false);

        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        if (authInfo != null && authInfo.IsAuthenticated())
            context.Operation.AddEvent(new UserSignedOutEvent(session.Id, command.Force, UserId.ParseOrNone(authInfo.UserId)));
    }
}
