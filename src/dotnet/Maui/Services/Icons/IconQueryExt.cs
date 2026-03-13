using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Media;

namespace ActualChat.Maui.Services;

public static class IconQueryExt
{
    public static IconQuery GetIconQuery(this Contact contact, int? avatarSize = null, bool renderAvatarTitle = false)
    {
        var chatKind = contact.ChatId.IsThread(out var threadChatId)
            ? threadChatId.ParentChatId.Kind
            : contact.ChatId.Kind;

        return chatKind switch {
            ChatKind.Peer => IconQuery.Create(
                contact.Account?.Avatar.Picture,
                AvatarKind.Beam,
                DefaultUserPicture.GetAvatarKey(contact.Account?.Id.Value ?? ""),
                avatarSize),
            ChatKind.Group or ChatKind.Place => IconQuery.Create(
                contact.Chat.Picture.ToPicture(),
                AvatarKind.Marble,
                contact.ChatId.Value,
                avatarSize,
                renderAvatarTitle ? GetInitial(contact.Chat.Title) : null),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    public static IconQuery GetIconQuery(this Place place, int? avatarSize = null, bool renderAvatarTitle = false)
        => IconQuery.Create(
            place.Picture.ToPicture(),
            AvatarKind.Marble,
            place.Id.Value,
            avatarSize,
            renderAvatarTitle ? GetInitial(place.Title) : null);

    private static string GetInitial(string title)
        => title.Length > 0 ? title[0].ToString().ToUpper() : "";
}
