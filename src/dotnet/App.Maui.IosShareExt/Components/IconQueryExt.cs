using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Media;
using ActualChat.UI;

namespace ActualChat.App.Maui.IosShareExt.Components;

public static class IconQueryExt
{
    public static IconQuery GetIconQuery(this Contact contact)
    {
        var chatKind = contact.ChatId.IsThread(out var threadChatId)
            ? threadChatId.ParentChatId.Kind
            : contact.ChatId.Kind;

        switch (chatKind) {
        case ChatKind.Peer:
            return new IconQuery(contact.Account?.Avatar.Picture, AvatarKind.Beam, contact.ChatId.Value);
        case ChatKind.Group:
        case ChatKind.Place:
            return new IconQuery(contact.Chat.Picture.ToPicture(), AvatarKind.Marble, contact.ChatId.Value);
        default:
            throw new ArgumentOutOfRangeException();
        }
    }

    public static IconQuery GetIconQuery(this Place place)
        => new (place.Picture.ToPicture(), AvatarKind.Marble, place.Id.Value);
}
