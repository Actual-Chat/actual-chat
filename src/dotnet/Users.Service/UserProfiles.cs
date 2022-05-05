using System.Security;
using ActualChat.Users.Db;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserProfiles : DbServiceBase<UsersDbContext>, IUserProfiles
{
    private readonly IAuth _auth;
    private readonly IUserProfilesBackend _backend;
    private readonly ICommander _commander;

    public UserProfiles(IServiceProvider services) : base(services)
    {
        _auth = services.GetRequiredService<IAuth>();
        _backend = services.GetRequiredService<IUserProfilesBackend>();
        _commander = services.GetRequiredService<ICommander>();
    }

    // [ComputeMethod]
    public virtual async Task<UserProfile?> Get(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return null;

        return await _backend.Get(user.Id, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task Update(IUserProfiles.UpdateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, userProfile) = command;

        await AssertCanUpdateUserProfile(session, userProfile, cancellationToken).ConfigureAwait(false);
        await _commander.Call(new IUserProfilesBackend.UpdateCommand(command.UserProfile), cancellationToken)
            .ConfigureAwait(false);
    }

    [CommandHandler(IsFilter = true, Priority = 1)]
    protected virtual async Task OnSignIn(SignInCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var isNewUser = context.Operation().Items.Get<bool>();
        if (!isNewUser)
            return;

        var userId = context.Operation().Items.Get<SessionInfo>()!.UserId;

        await _commander
            .Call(new IUserProfilesBackend.CreateCommand(userId), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AssertCanUpdateUserProfile(
        Session session,
        UserProfile userProfileToUpdate,
        CancellationToken cancellationToken)
    {
        var currentUserProfile = await Get(session, cancellationToken).ConfigureAwait(false)
            ?? throw new Exception("User profile not found");
        if (userProfileToUpdate.User.Id == currentUserProfile.Id) {
            if (currentUserProfile.Status != userProfileToUpdate.Status)
                throw new SecurityException("User cannot update it's own status");

            return;
        }

        if (currentUserProfile.IsAdmin)
            return;

        throw new UnauthorizedAccessException(
            $"User id='{currentUserProfile.User.Id}' is not allowed to update status of user id='{userProfileToUpdate.Id}'");
    }
}
