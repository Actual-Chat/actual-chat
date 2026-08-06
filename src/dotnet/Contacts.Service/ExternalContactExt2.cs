using ActualChat.Contacts.Db;

namespace ActualChat.Contacts;

internal static class ExternalContactExt2
{
    public static async Task<UserId?> FindUser(IAccountsBackend accountsBackend, string externalContactLinkHashValue, CancellationToken cancellationToken)
    {
        if (DbExternalContactLink.IsPhoneLink(externalContactLinkHashValue, out var phoneHash))
            return await accountsBackend.GetUserIdByPhoneHash(phoneHash, cancellationToken).ConfigureAwait(false);

        if (DbExternalContactLink.IsEmailLink(externalContactLinkHashValue, out var emailHash))
            return await accountsBackend.GetUserIdByEmailHash(emailHash, cancellationToken).ConfigureAwait(false);

        return null;
    }
}
