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
        await this.AssertCanRead(session, userId, cancellationToken).ConfigureAwait(false);
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

        await this.AssertCanUpdate(session, userProfile, cancellationToken).ConfigureAwait(false);
        await _commander.Call(new IUserProfilesBackend.UpdateCommand(command.UserProfile), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AssertCanUpdateUserProfile(
        Session session,
        UserProfile userProfileToUpdate,
        CancellationToken cancellationToken)
    {
        var userProfile = await this.Require(session, cancellationToken).ConfigureAwait(false);
        if (userProfileToUpdate.User.Id == userProfile.Id) {
            if (userProfile.Status != userProfileToUpdate.Status)
                throw new SecurityException("You can't update your own status.");
            return;
        }

        userProfile.AssertAdmin();
    }

    private async Task AssertCanReadUserProfile(
        Session session,
        string userId,
        CancellationToken cancellationToken)
    {
        var userProfile = await this.Require(session, cancellationToken).ConfigureAwait(false);
        if (userProfile.User.Id == userId)
            return;
        if (userProfile.IsActive())
            return;

        throw new SecurityException("You can't access the profile of another user.");
    }
}
