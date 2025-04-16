using ActualChat.Users;

namespace ActualChat.UI.Blazor;

public static class LinksExt
{
    public static LocalUrl User(AccountFull account)
    {
        if (account.UserLinkId is { } userLinkId)
            return Links.AccountUserLinkPrefix + userLinkId;

        return Links.User(account.Id);
    }
}
