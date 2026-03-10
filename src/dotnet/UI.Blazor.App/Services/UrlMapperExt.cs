namespace ActualChat.UI.Blazor.App.Services;

public static class UrlMapperExt
{
    public static string AudioBlobUrl(this UrlMapper urlMapper, ChatEntry audioEntry)
    {
        var contentId = audioEntry.Audio?.BlobId ?? audioEntry.Content;
        return urlMapper.AudioBlobUrl(contentId);
    }

    public static string AudioBlobUrl(this UrlMapper urlMapper, string contentId)
    {
        if (contentId.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(contentId), "Content ID is empty.");

        return urlMapper.ToAbsolute(urlMapper.ApiBaseUrl, "audio/download/" + contentId);
    }
}
