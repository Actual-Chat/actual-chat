﻿namespace ActualChat.Users;

public class UserAvatars : IUserAvatars
{
    private readonly IAuth _auth;
    private readonly IUserAvatarsBackend _userAvatarsBackend;
    private readonly IUserProfilesBackend _userProfilesBackend;
    private readonly ICommander _commander;

    public UserAvatars(
        IAuth auth,
        IUserAvatarsBackend userAvatarsBackend,
        IUserProfilesBackend userProfilesBackend,
        ICommander commander)
    {
        _auth = auth;
        _userAvatarsBackend = userAvatarsBackend;
        _userProfilesBackend = userProfilesBackend;
        _commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<UserAvatar?> Get(Session session, string avatarId, CancellationToken cancellationToken)
    {
        if (avatarId.IsNullOrEmpty())
            return null;

        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var userAvatarRequested = avatarId.OrdinalStartsWith("0:");
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
        var user = await _auth.RequireUser(session, cancellationToken).ConfigureAwait(false);
        return await _userAvatarsBackend.GetAvatarIds(user.Id, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<string> GetDefaultAvatarId(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.RequireUser(session, cancellationToken).ConfigureAwait(false);
        var profile = await _userProfilesBackend.Get(user.Id, cancellationToken).ConfigureAwait(false);
        return profile?.AvatarId ?? "";
    }

    public virtual async Task SetDefault(IUserAvatars.SetDefaultCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var user = await _auth.RequireUser(command.Session, cancellationToken).ConfigureAwait(false);
        var avatarId = command.AvatarId;
        if (!avatarId.IsNullOrEmpty()) {
            var avatar = await _userAvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
            if (avatar == null || avatar.UserId != user.Id)
                throw new InvalidOperationException("Invalid AvatarId");
        }

        var userProfile = await _userProfilesBackend.Get(user.Id, cancellationToken).ConfigureAwait(false);
        if (userProfile == null)
            throw new InvalidOperationException("User profile does not exist");
        userProfile = userProfile with { AvatarId = avatarId };
        var updateCommand = new IUserProfilesBackend.UpdateCommand(userProfile);
        await _commander.Call(updateCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<UserAvatar> Create(IUserAvatars.CreateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var session = command.Session;
        var user = await _auth.RequireUser(session, cancellationToken).ConfigureAwait(false);
        var cmd = new IUserAvatarsBackend.CreateCommand( user.Id, user.Name);
        var userAvatar = await _commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        return userAvatar;
    }

    // [CommandHandler]
    public virtual async Task Update(IUserAvatars.UpdateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var avatarId = command.AvatarId;
        var avatar = await Get(command.Session, avatarId, cancellationToken).ConfigureAwait(false);
        if (avatar == null)
            throw new InvalidOperationException("Invalid avatar id");

        var updateCommand = new IUserAvatarsBackend.UpdateCommand(avatarId, command.Name, command.Picture, command.Bio);
        await _commander.Call(updateCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
