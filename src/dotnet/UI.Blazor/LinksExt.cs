using ActualChat.Users;

namespace ActualChat.UI.Blazor;

public static class LinksExt
{
    public static LocalUrl User(AccountFull account)
    {
        if (!account.UserLinkId.IsNone)
            return Links.AccountUserLinkPrefix + account.UserLinkId.Value;

        return Links.User(account.Id);
    }
}
