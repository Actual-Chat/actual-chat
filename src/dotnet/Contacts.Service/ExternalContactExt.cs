namespace ActualChat.Contacts;

internal static class ExternalContactExt
{
    public static string PickDisplayName(
        this IEnumerable<ExternalContactFull?> externalContacts,
        AccountFull peerAccount)
    {
        // An entry holding only the peer's email is often a forgotten or auto-collected one.
        // ModifiedAt is the server time of the last sync of the entry, so it only breaks ties.
        var phoneHashes = peerAccount.Identities.GetPhoneHashes();
        var externalContact = externalContacts
            .SkipNullItems()
            .Where(c => !c.DisplayName.IsNullOrEmpty())
            .OrderByDescending(c => phoneHashes.Any(c.PhoneHashes.Contains))
            .ThenByDescending(c => c.ModifiedAt)
            .FirstOrDefault();
        return externalContact?.DisplayName ?? "";
    }
}
