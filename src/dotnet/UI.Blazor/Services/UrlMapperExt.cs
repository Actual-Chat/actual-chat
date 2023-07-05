using ActualChat.Users;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.UI.Blazor.Services;

public static class UrlMapperExt
{
    public static string UserPictureUrl(this UrlMapper mapper, UserPicture userPicture)
    {
        var contentId = userPicture.ContentId;
        if (!contentId.IsNullOrEmpty())
        {
            var contentUrl = mapper.ContentUrl(contentId);
            return mapper.ImagePreview128Url(contentUrl);
        }

        var avatarPicture = userPicture.Picture;
        if (avatarPicture.IsNullOrEmpty())
            return "";

        if (avatarPicture.OrdinalStartsWith(DefaultUserPicture.BoringAvatarsBaseUrl))
            return mapper.BoringAvatar(avatarPicture);

        if (avatarPicture.OrdinalStartsWith("http"))
            return mapper.ImagePreview128Url(avatarPicture);

        return "";
    }

    public static string AvatarPictureUrl(this UrlMapper mapper, Avatar avatar)
    {
        return UserPictureUrl(mapper, avatar.UserPicture).NullIfEmpty() ?? Default();

        string Default()
        {
            var hash = avatar.Id.ToString().GetDjb2HashCode().ToString(CultureInfo.InvariantCulture);
            return mapper.BoringAvatar(DefaultUserPicture.GetBoringAvatar(hash));
        }
    }
}
