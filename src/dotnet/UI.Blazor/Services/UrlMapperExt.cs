using ActualChat.Users;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.UI.Blazor.Services;

public static class UrlMapperExt
{
    public static string PicturePreview128Url(this UrlMapper mapper, Picture? picture)
    {
        if (picture is null)
            return "";

        var pictureUrl = mapper.PictureUrl(picture);
        if (pictureUrl.OrdinalStartsWith(DefaultUserPicture.BoringAvatarsBaseUrl))
            return mapper.BoringAvatar(pictureUrl);

        return mapper.ImagePreview128Url(pictureUrl);    }

    public static string PictureUrl(this UrlMapper mapper, Picture picture)
        => picture.MediaContent != null
            ? mapper.ContentUrl(picture.MediaContent.ContentId)
            : picture.ExternalUrl ?? "";


    public static string AvatarPicturePreview128Url(this UrlMapper mapper, Avatar avatar)
    {
        return PicturePreview128Url(mapper, avatar.Picture).NullIfEmpty() ?? Default();

        string Default()
        {
            var hash = avatar.Id.ToString().GetDjb2HashCode().ToString(CultureInfo.InvariantCulture);
            return mapper.BoringAvatar(DefaultUserPicture.GetBoringAvatar(hash));
        }
    }
}
