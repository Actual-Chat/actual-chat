namespace ActualChat;

// TODO: probably can be merged with IconQueryExt from Maui.csproj
/// <summary>
/// Extension methods for creating <see cref="IconQuery"/> from domain objects.
/// </summary>
public static class IconQueryExt
{
    public static IconQuery GetIconQuery(
        this Chat.Chat chat,
        AuthorFull? author = null,
        int? avatarSize = null,
        bool renderAvatarTitle = false)
    {
        var chatKind = chat.Id.IsThread(out var threadChatId)
            ? threadChatId.ParentChatId.Kind
            : chat.Id.Kind;

        return chatKind switch {
            ChatKind.Peer when author != null => IconQuery.Create(
                author.Avatar.Picture,
                AvatarKind.Beam,
                DefaultUserPicture.GetAvatarKey(author.UserId.Value),
                avatarSize),
            ChatKind.Group or ChatKind.Place => IconQuery.Create(
                chat.Picture.ToPicture(),
                AvatarKind.Marble,
                chat.Id.Value,
                avatarSize,
                renderAvatarTitle ? GetInitial(chat.Title) : null),
            _ => IconQuery.Create(
                null,
                AvatarKind.Marble,
                chat.Id.Value,
                avatarSize),
        };
    }

    private static string? GetInitial(string? title)
    {
        if (title.IsNullOrEmpty())
            return null;

        foreach (var c in title) {
            if (char.IsLetterOrDigit(c))
                return c.ToString().ToUpperInvariant();
        }
        return null;
    }
}
