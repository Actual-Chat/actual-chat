namespace ActualChat.Contacts;

internal static class ContactExt
{
    public static bool IsChatRoulette(this ActualChat.Contacts.Contact contact)
        => Equals(Constants.Contact.SystemTags.ChatRoulette, contact.SystemTag);
}
