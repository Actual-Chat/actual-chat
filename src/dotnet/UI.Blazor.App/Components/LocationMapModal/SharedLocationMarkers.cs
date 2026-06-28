using ActualChat.Chat;
using ActualChat.UI.Blazor.Components;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Components;
public static class SharedLocationMarkers
{
    public static async Task<MapMarker> ToAvatarMarker(
        this SharedLocation location,
        Session session,
        IAuthors authors,
        UrlMapper urlMapper,
        CancellationToken cancellationToken)
    {
        var author = await authors.Get(session, location.ChatId, location.AuthorId, cancellationToken).ConfigureAwait(false);
        var avatar = author?.Avatar;
        var pictureUrl = avatar?.Picture is { } picture ? urlMapper.PicturePreview128Url(picture) : "";
        var avatarKey = pictureUrl.IsNullOrEmpty()
            ? (avatar?.Picture?.AvatarKey).NullIfEmpty() ?? DefaultUserPicture.GetAvatarKey(location.AuthorId.Value)
            : "";
        return new MapMarker(
            location.Id.Value,
            location.Point.Latitude,
            location.Point.Longitude,
            avatar?.Name,
            pictureUrl.NullIfEmpty(),
            avatarKey.NullIfEmpty());
    }
}
