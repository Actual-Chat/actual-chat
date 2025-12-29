namespace ActualChat;

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

        private string PictureUrl(Picture picture)
            => picture.MediaContent != null
                ? mapper.ContentUrl(picture.MediaContent.ContentId)
                : picture.ExternalUrl ?? "";
    }
}
