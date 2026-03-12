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

        public string AvatarUrl(AvatarQuery query)
        {
            var kindPath = query.Kind is AvatarKind.Marble ? "marble" : "beam";
            var url = $"api/avatars/{kindPath}/{Uri.EscapeDataString(query.Key)}";
            var separator = '?';
            if (query.Format != AvatarFormat.Svg) {
                url += $"{separator}format={query.Format.ToString().ToLowerInvariant()}";
                separator = '&';
            }
            if (query.Size > 0) {
                url += $"{separator}size={query.Size}";
                separator = '&';
            }
            if (query.Kind is AvatarKind.Marble && !query.Title.IsNullOrEmpty())
                url += $"{separator}title={Uri.EscapeDataString(query.Title)}";
            return mapper.ToAbsolute(url);
        }

        private string PictureUrl(Picture picture)
            => picture.MediaRef != null
                ? mapper.ContentUrl(picture.MediaRef.BlobId)
                : picture.ExternalUrl ?? "";
    }
}
