namespace ActualChat.Users;

public class UserAuthors : IUserAuthors
{
    private readonly IUserAuthorsBackend _userAuthorsBackend;
    private readonly IAuth _auth;
    private readonly ICommander _cmd;

    public UserAuthors(IUserAuthorsBackend userAuthorsBackend, IAuth auth, ICommander cmd)
    {
        _userAuthorsBackend = userAuthorsBackend;
        _auth = auth;
        _cmd = cmd;
    }

    // [ComputeMethod]
    public virtual Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken)
        => _userAuthorsBackend.Get(userId, inherit, cancellationToken);

    public virtual async Task<string> GetName(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var userAuthor = await Get(user.Id, false, cancellationToken).ConfigureAwait(false);
        return userAuthor!.Name;
    }

    public virtual async Task UpdateName(IUserAuthors.UpdateNameCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;
        var user = await _auth.GetUser(command.Session, cancellationToken).ConfigureAwait(false);
        var updateCommand = new IUserAuthorsBackend.UpdateNameCommand(user.Id, command.Name);
        await _cmd.Call(updateCommand, cancellationToken).ConfigureAwait(false);
    }
}
