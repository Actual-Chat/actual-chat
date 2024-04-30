using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class AuthCommandFilters(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), ICommandService
{
    protected UserNamer UserNamer { get; } = services.GetRequiredService<UserNamer>();

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
}
