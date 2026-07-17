namespace ActualChat.UI.Blazor.Components;

public sealed record MapMarker(
    string Id,
    GeoPoint Point,
    string? Label = null,
    string? AvatarUrl = null,
    string? AvatarKey = null,
    bool IsOwnLocation = false)
{
    // TODO: move this logic to LocationUI when there is a GetMapMarker(locationId)
    public static MapMarker ForAuthor(string id, GeoPoint point, Author author, UrlMapper urlMapper)
    {
        var pictureUrl = author.Avatar.Picture is { } picture ? urlMapper.PicturePreview128Url(picture) : "";
        var avatarKey = pictureUrl.IsNullOrEmpty()
            ? (author.Avatar.Picture?.AvatarKey).NullIfEmpty() ?? DefaultUserPicture.GetAvatarKey(author.Id.Value)
            : "";
        return new MapMarker(id, point, author.Avatar.Name, pictureUrl.NullIfEmpty(), avatarKey.NullIfEmpty());
    }
}
