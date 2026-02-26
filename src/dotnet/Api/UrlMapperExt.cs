using ActualChat.UI;

namespace ActualChat;

/// <summary>
/// Extension methods for <see cref="UrlMapper"/> to generate picture preview URLs.
/// </summary>
public static class UrlMapperExt
{
    extension(UrlMapper mapper)
    {
        public string PicturePreview128Url(Picture? picture)
        {
            if (picture is null)
                return "";

            var pictureUrl = mapper.PictureUrl(picture);
            if (pictureUrl.IsNullOrEmpty())
                return "";

            return mapper.ImagePreview128Url(pictureUrl);
        }

        public string PicturePreviewUrl(Picture? picture)
        {
            if (picture is null)
                return "";

            var pictureUrl = mapper.PictureUrl(picture);
            if (pictureUrl.IsNullOrEmpty())
                return "";

            return mapper.ImagePreviewUrl(pictureUrl, (int?)Constants.Attachments.MaxResolution.X, (int?)Constants.Attachments.MaxResolution.Y);
        }

        public string AvatarFullSizePreviewUrl(Picture? picture)
        {
            if (picture is null)
                return "";

            var pictureUrl = mapper.PictureUrl(picture);
            if (pictureUrl.IsNullOrEmpty())
                return "";

            return mapper.ImagePreviewUrl(pictureUrl, (int?)Constants.Attachments.MaxAvatarResolution.X, (int?)Constants.Attachments.MaxAvatarResolution.Y);
        }

        public string AvatarPngUrl(AvatarKind kind, string key, int? size = null, string? title = null)
        {
            var kindPath = kind is AvatarKind.Marble ? "marble" : "beam";
            var url = $"api/avatars/{kindPath}/{Uri.EscapeDataString(key)}?format=png";
            if (size > 0)
                url += $"&size={size}";
            if (kind is AvatarKind.Marble && !title.IsNullOrEmpty())
                url += $"&title={Uri.EscapeDataString(title)}";
            return mapper.ToAbsolute(url);
        }

        private string PictureUrl(Picture picture)
            => picture.MediaRef != null
                ? mapper.ContentUrl(picture.MediaRef.BlobId)
                : picture.ExternalUrl ?? "";
    }
}
