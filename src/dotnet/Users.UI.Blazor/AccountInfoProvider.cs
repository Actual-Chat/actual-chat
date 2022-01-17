namespace ActualChat.Users.UI.Blazor;

internal class AccountInfoProvider : IAccountInfoProvider
{
    private readonly IUserAuthors _userAuthors;

    public AccountInfoProvider(IUserAuthors userAuthors) => _userAuthors = userAuthors;

    public async Task<AccountInfo?> GetAccountInfo(User user, CancellationToken cancellationToken)
    {
        var userAuthor = await _userAuthors.Get(user.Id, true, cancellationToken).ConfigureAwait(false);
        if (userAuthor == null)
            return null;
        return new AccountInfo(userAuthor.Name, userAuthor.Picture);
    }
}
