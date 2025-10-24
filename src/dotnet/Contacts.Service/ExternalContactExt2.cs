using ActualChat.Contacts.Db;
using ActualChat.Users;

namespace ActualChat.Contacts;

internal static class ExternalContactExt2
{
    public static async Task<UserId?> FindUser(IAccountsBackend accountsBackend, string externalContactLinkHashValue, CancellationToken cancellationToken)
    {
        if (DbExternalContactLink.IsPhoneLink(externalContactLinkHashValue, out var phoneHash))
            return await accountsBackend.GetIdByPhoneHash(phoneHash, cancellationToken).ConfigureAwait(false);

        if (DbExternalContactLink.IsEmailLink(externalContactLinkHashValue, out var emailHash))
            return await accountsBackend.GetIdByEmailHash(emailHash, cancellationToken).ConfigureAwait(false);

        return null;
    }
}
