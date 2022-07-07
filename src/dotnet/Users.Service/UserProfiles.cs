using System.Security;
using ActualChat.Users.Db;
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
        if (user == null)
            return null;

        return await _backend.Get(user.Id, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<UserProfile?> GetByUserId(Session session, string userId, CancellationToken cancellationToken)
    {
        await AssertCanReadUserProfile(session, userId, cancellationToken).ConfigureAwait(false);
        return await _backend.Get(userId, cancellationToken).ConfigureAwait(false);
    }

    public virtual Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken)
        => _backend.GetUserAuthor(userId, cancellationToken);

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

    private async Task AssertCanReadUserProfile(
        Session session,
        string userId,
        CancellationToken cancellationToken)
    {
        var currentUserProfile = await Get(session, cancellationToken).ConfigureAwait(false)
            ?? throw new Exception("User profile not found");
        if (currentUserProfile.User.Id == userId)
            return;
        if (currentUserProfile.Status == UserStatus.Active)
            return;
        throw new SecurityException("User cannot read other profiles");
    }
}
