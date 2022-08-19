namespace ActualChat.Users;

public class UserAvatars : IUserAvatars
{
    private readonly IAuth _auth;
    private readonly IAccounts _accounts;
    private readonly IAccountsBackend _accountsBackend;
    private readonly IUserAvatarsBackend _userAvatarsBackend;
    private readonly ICommander _commander;

    public UserAvatars(
        IAuth auth,
        IAccounts accounts,
        IAccountsBackend accountsBackend,
        IUserAvatarsBackend userAvatarsBackend,
        ICommander commander)
    {
        _auth = auth;
        _accounts = accounts;
        _accountsBackend = accountsBackend;
        _userAvatarsBackend = userAvatarsBackend;
        _commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<UserAvatar?> Get(Session session, string avatarId, CancellationToken cancellationToken)
    {
        if (avatarId.IsNullOrEmpty())
            return null;

        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return null;

        var userAvatarRequested = avatarId.OrdinalStartsWith("0:");
        if (userAvatarRequested)
            user.Require();

        var userAvatar = await _userAvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
        if (userAvatar == null)
            return null;

        if (userAvatarRequested && userAvatar.UserId != user.Id)
            return null;
        return userAvatar;
    }

    // [ComputeMethod]
    public virtual async Task<Symbol> GetDefaultAvatarId(Session session, CancellationToken cancellationToken)
    {
        var account = await _accounts.Get(session, cancellationToken).Require().ConfigureAwait(false);
        return account.AvatarId;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListAvatarIds(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).Require().ConfigureAwait(false);
        return await _userAvatarsBackend.ListAvatarIds(user.Id, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task SetDefault(IUserAvatars.SetDefaultCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var account = await _accounts.Get(command.Session, cancellationToken).Require().ConfigureAwait(false);
        var avatarId = command.AvatarId;
        if (!avatarId.IsNullOrEmpty()) {
            var avatar = await _userAvatarsBackend.Get(avatarId, cancellationToken).ConfigureAwait(false);
            if (avatar == null || avatar.UserId != account.Id)
                throw StandardError.Constraint("Invalid AvatarId.");
        }

        account = account with { AvatarId = avatarId };
        var updateCommand = new IAccountsBackend.UpdateCommand(account);
        await _commander.Call(updateCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<UserAvatar> Create(IUserAvatars.CreateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!;

        var session = command.Session;
        var user = await _auth.GetUser(session, cancellationToken).Require().ConfigureAwait(false);
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
        _ = await Get(command.Session, avatarId, cancellationToken).Require().ConfigureAwait(false);

        var updateCommand = new IUserAvatarsBackend.UpdateCommand(avatarId, command.Name, command.Picture, command.Bio);
        await _commander.Call(updateCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
