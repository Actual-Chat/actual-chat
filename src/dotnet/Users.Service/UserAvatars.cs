namespace ActualChat.Users;

public class UserAvatars : IUserAvatars
{
    private readonly IAuth _auth;
    private readonly IUserAvatarsBackend _userAvatarsBackend;
    private readonly IUserAuthorsBackend _userAuthorsBackend;
    private readonly IUserProfilesBackend _userProfilesBackend;
    private readonly ICommander _commander;

    public UserAvatars(
        IAuth auth,
        IUserAvatarsBackend userAvatarsBackend,
        IUserAuthorsBackend userAuthorsBackend,
        IUserProfilesBackend userProfilesBackend,
        ICommander commander)
    {
        _auth = auth;
        _userAvatarsBackend = userAvatarsBackend;
        _userAuthorsBackend = userAuthorsBackend;
        _userProfilesBackend = userProfilesBackend;
        _commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<UserAvatar?> Get(Session session, string avatarId, CancellationToken cancellationToken)
    {
        if (avatarId.IsNullOrEmpty())
            return null;

        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var userAvatarRequested = avatarId.StartsWith("0:", StringComparison.Ordinal);
        if (userAvatarRequested)
            user.MustBeAuthenticated();

        var userAvatar = await _userAvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
        if (userAvatar == null)
            return null;

        if (userAvatarRequested && userAvatar.UserId != user.Id)
            return null;
        return userAvatar;
    }

    public virtual async Task<string[]> GetAvatarIds(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        return await _userAvatarsBackend.GetAvatarIds(user.Id, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<string> GetDefaultAvatarId(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        var userAuthor = await _userAuthorsBackend.Get(user.Id, false, cancellationToken).ConfigureAwait(false);
        return userAuthor?.AvatarId ?? "";
    }

    public virtual async Task SetDefault(IUserAvatars.SetDefaultCommand command, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(command.Session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        var avatarId = command.AvatarId;
        if (!avatarId.IsNullOrEmpty()) {
            var avatar = await _userAvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
            if (avatar == null || avatar.UserId != user.Id)
                throw new InvalidOperationException("Invalid AvatarId");
        }

        var setCommand = new IUserAuthorsBackend.SetAvatarCommand(user.Id, avatarId);
        await _commander.Call(setCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<UserAvatar> Create(IUserAvatars.CreateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var session = command.Session;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        user.MustBeAuthenticated();

        var userInfo = await _userProfilesBackend.Get(user.Id, cancellationToken).ConfigureAwait(false);
        var userName = userInfo?.Name ?? user.Name;

        var createCommand = new IUserAvatarsBackend.CreateCommand( user.Id, userName);
        var userAvatar = await _commander.Call(createCommand, true, cancellationToken).ConfigureAwait(false);
        return userAvatar;
    }

    // [CommandHandler]
    public virtual async Task Update(IUserAvatars.UpdateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var session = command.Session;
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        //user.MustBeAuthenticated();

        var avatarId = command.AvatarId;
        var avatar = await Get(session, avatarId, cancellationToken).ConfigureAwait(false);
        if (avatar == null)
            throw new InvalidOperationException("Invalid avatar id");

        var updateCommand = new IUserAvatarsBackend.UpdateCommand(avatarId, command.Name, command.Picture, command.Bio);
        await _commander.Call(updateCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
