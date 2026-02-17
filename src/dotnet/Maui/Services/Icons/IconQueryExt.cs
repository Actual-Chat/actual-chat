using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Media;
using ActualChat.UI;

namespace ActualChat.Maui.Services;

public static class IconQueryExt
{
    public static IconQuery GetIconQuery(this Contact contact, int? avatarSize = null, bool renderAvatarTitle = false)
    {
        var chatKind = contact.ChatId.IsThread(out var threadChatId)
            ? threadChatId.ParentChatId.Kind
            : contact.ChatId.Kind;

        switch (chatKind) {
        case ChatKind.Peer:
            var defaultAvatarKey = DefaultUserPicture.GetAvatarKey(contact.Account?.Id.Value ?? "");
            return new IconQuery(contact.Account?.Avatar.Picture, AvatarKind.Beam, defaultAvatarKey, avatarSize);
        case ChatKind.Group:
        case ChatKind.Place:
            return new IconQuery(contact.Chat.Picture.ToPicture(),
                AvatarKind.Marble,
                contact.ChatId.Value,
                avatarSize,
                renderAvatarTitle ? GetInitial(contact.Chat.Title) : "");
        default:
            throw new ArgumentOutOfRangeException();
        }
    }

    public static IconQuery GetIconQuery(this Place place, int? avatarSize = null, bool renderAvatarTitle = false)
        => new (place.Picture.ToPicture(),
            AvatarKind.Marble,
            place.Id.Value,
            avatarSize,
            renderAvatarTitle ? GetInitial(place.Title) : "");

    private static string GetInitial(string title)
        => title.Length > 0 ? title[0].ToString().ToUpperInvariant() : "";
}
