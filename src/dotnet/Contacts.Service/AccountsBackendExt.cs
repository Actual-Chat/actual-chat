using ActualChat.Contacts.Db;

namespace ActualChat.Contacts;

internal static class AccountsBackendExt
{
    public static async Task<UserId?> GetUserIdByExternalContactLink(
        this IAccountsBackend accountsBackend,
        string link,
        CancellationToken cancellationToken)
    {
        if (DbExternalContactLink.IsPhoneLink(link, out var phoneHash))
            return await accountsBackend.GetUserIdByPhoneHash(phoneHash, cancellationToken).ConfigureAwait(false);

        if (DbExternalContactLink.IsEmailLink(link, out var emailHash))
            return await accountsBackend.GetUserIdByEmailHash(emailHash, cancellationToken).ConfigureAwait(false);

        return null;
    }
}
