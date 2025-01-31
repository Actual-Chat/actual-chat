namespace ActualChat.Contacts;

public static class ContactExt
{
    public static string GetPeerContactName(Contact? contact, string peerAvatarName)
        => !string.IsNullOrWhiteSpace(contact?.PeerContactName)
            ? contact.PeerContactName
            : peerAvatarName;
}
