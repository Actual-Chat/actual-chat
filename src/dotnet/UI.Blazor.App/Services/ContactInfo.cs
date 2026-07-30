using ActualChat.Contacts;
using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// A contact-only view model for the contact pickers: everything they order, filter and search
/// by lives on <see cref="Contacts.Contact"/>, so unlike <see cref="ChatInfo"/> this one needs
/// no per-chat news, read positions or markup.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record ContactInfo(Contact Contact) : IHasId<ChatId>
{
    public ChatId Id => Contact.Id.ChatId;
    public Chat.Chat Chat => Contact.Chat;
    public SearchDocument SearchDocument => field = field.OrNew(Chat.Title);

    // This record relies on referential equality
    public bool Equals(ContactInfo? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
