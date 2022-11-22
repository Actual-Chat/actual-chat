using ActualChat.Contacts;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed record ContactSummary(
    Contact Contact,
    bool HasMentions = false,
    int UnreadMessageCount = 0,
    bool IsVirtual = false)
{
    public static implicit operator ContactSummary(Contact contact) => new(contact);

    // Equality must rely on Id only
    public bool Equals(ContactSummary? other)
        => other != null && Contact.Id.Equals(other.Contact.Id);
    public override int GetHashCode()
        => Contact.Id.GetHashCode();
}
