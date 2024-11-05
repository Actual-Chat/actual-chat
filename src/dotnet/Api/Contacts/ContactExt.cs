namespace ActualChat.Contacts;

public static class ContactExt
{
    public static string GetPeerContactName(Contact? contact, string peerAvatarName)
    {
        if (contact is null || contact.PeerContactName.IsNullOrEmpty())
            return peerAvatarName;

        return contact.PeerContactName + " aka. " + peerAvatarName;
    }
}
