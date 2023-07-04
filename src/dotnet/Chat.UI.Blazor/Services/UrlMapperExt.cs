using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public static class UrlMapperExt
{
    public static string AudioBlobUrl(this UrlMapper urlMapper, ChatEntry audioEntry)
    {
        if (audioEntry.Kind != ChatEntryKind.Audio)
            throw new ArgumentOutOfRangeException(nameof(audioEntry),
                $"Only Audio entries are supported, but an entry of {audioEntry.Kind.ToString()} type was provides.");
        if (audioEntry.Content.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(audioEntry),
                $"{nameof(audioEntry)} doesn't have Content.");

        return urlMapper.ToAbsolute(urlMapper.ApiBaseUrl, "audio/download/" + audioEntry.Content);
    }

    public static string UserPictureUrl(this UrlMapper mapper, UserPicture picture)
        => picture.ContentId.IsNullOrEmpty() ? picture.Picture ?? "" : mapper.ContentUrl(picture.ContentId);
}
