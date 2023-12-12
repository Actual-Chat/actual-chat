using ActualChat.UI.Blazor.Services;

namespace ActualChat.Users.UI.Blazor;

public static class ShareUIExt
{
    public static async Task<ModalRef?> ShareOwnAccount(
        this ShareUI shareUI, CancellationToken cancellationToken = default)
    {
        var shareModel = await shareUI.GetOwnAccountModel(cancellationToken).ConfigureAwait(true);
        return shareModel == null ? null
            : await shareUI.Share(shareModel).ConfigureAwait(false);
    }

    public static async Task<ModalRef?> Share(
        this ShareUI shareUI, UserId userId, CancellationToken cancellationToken = default)
    {
        var shareModel = await shareUI.GetModel(userId, cancellationToken).ConfigureAwait(true);
        return shareModel == null ? null
            : await shareUI.Share(shareModel).ConfigureAwait(false);
    }

    public static async ValueTask<ShareModalModel?> GetOwnAccountModel(
        this ShareUI shareUI, CancellationToken cancellationToken = default)
    {
        var accountUI = shareUI.Hub.AccountUI;
        await accountUI.WhenLoaded.WaitAsync(cancellationToken).ConfigureAwait(false);
        var ownAccount = accountUI.OwnAccount.Value;
        if (ownAccount.IsGuestOrNone)
            return null;

        var name = ownAccount.Avatar.Name;
        var title = "Share your contact";
        var text = $"{name} on Actual Chat";
        return new ShareModalModel(
            ShareKind.Contact, title, name,
            new(text, Links.User(ownAccount.Id)));
    }

    public static async ValueTask<ShareModalModel?> GetModel(
        this ShareUI shareUI, UserId userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsGuestOrNone)
            return null;

        var hub = shareUI.Hub;
        var accountUI = hub.GetRequiredService<AccountUI>();
        await accountUI.WhenLoaded.WaitAsync(cancellationToken).ConfigureAwait(false);
        var ownAccount = accountUI.OwnAccount.Value;
        if (userId == ownAccount.Id)
            return await shareUI.GetOwnAccountModel(cancellationToken).ConfigureAwait(false);

        var session = hub.Session();
        var accounts = hub.GetRequiredService<IAccounts>();
        var account = await accounts.Get(session, userId, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        var name = account.Avatar.Name;
        var title = $"Share {name}'s contact";
        var text = $"{name} on Actual Chat";
        return new ShareModalModel(
            ShareKind.Contact, title, name,
            new(text, Links.User(account.Id)));
    }
}
