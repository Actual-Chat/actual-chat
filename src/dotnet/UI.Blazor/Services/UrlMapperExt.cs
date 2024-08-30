namespace ActualChat.UI.Blazor.Services;

public static class UrlMapperExt
{
    public static string PicturePreview128Url(this UrlMapper mapper, Picture? picture)
    {
        if (picture is null)
            return "";

        var pictureUrl = mapper.PictureUrl(picture);
        if (pictureUrl.IsNullOrEmpty())
            return "";

        return mapper.ImagePreview128Url(pictureUrl);
    }

    public static string PicturePreviewUrl(this UrlMapper mapper, Picture? picture)
    {
        if (picture is null)
            return "";

        var pictureUrl = mapper.PictureUrl(picture);
        if (pictureUrl.IsNullOrEmpty())
            return "";

        return mapper.ImagePreviewUrl(pictureUrl, (int?)Constants.Attachments.MaxResolution.X, (int?)Constants.Attachments.MaxResolution.Y);
    }

    private static string PictureUrl(this UrlMapper mapper, Picture picture)
        => picture.MediaContent != null
            ? mapper.ContentUrl(picture.MediaContent.ContentId)
            : picture.ExternalUrl ?? "";
}
