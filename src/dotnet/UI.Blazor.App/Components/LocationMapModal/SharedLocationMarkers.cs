using ActualChat.Chat;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Components;
public static class SharedLocationMarkers
{
    public const string OwnColor = "#16a34a";
    public const string OtherColor = "#2563eb";

    public static async Task<MapMarker> ToMapMarker(
        this SharedLocation location,
        Session session,
        AuthorId? ownAuthorId,
        IAuthors authors,
        UrlMapper urlMapper,
        LiveTime liveTime,
        CancellationToken cancellationToken)
    {
        var author = await authors.Get(session, location.ChatId, location.AuthorId, cancellationToken).ConfigureAwait(false);
        var isOwn = ownAuthorId is { } ownId && location.AuthorId == ownId;
        var avatar = author?.Avatar;
        var pictureUrl = avatar?.Picture is { } picture ? urlMapper.PicturePreview128Url(picture) : "";
        var avatarKey = pictureUrl.IsNullOrEmpty()
            ? (avatar?.Picture?.AvatarKey).NullIfEmpty() ?? DefaultUserPicture.GetAvatarKey(location.AuthorId.Value)
            : "";
        var updatedAt = await liveTime.GetDeltaText(location.ModifiedAt, cancellationToken).ConfigureAwait(false);
        return new MapMarker(
            location.Id.Value,
            location.Point.Latitude,
            location.Point.Longitude,
            isOwn ? "You" : avatar?.Name,
            isOwn ? OwnColor : OtherColor,
            $"updated {updatedAt}",
            pictureUrl.NullIfEmpty(),
            avatarKey.NullIfEmpty());
    }
}
