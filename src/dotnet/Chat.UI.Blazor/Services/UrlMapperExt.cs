namespace ActualChat.Chat.UI.Blazor.Services;

public static class UrlMapperExt
{
    public static string AudioBlobUrl(this UrlMapper urlMapper, ChatEntry audioEntry)
    {
        if (audioEntry.Type != ChatEntryType.Audio)
            throw new ArgumentOutOfRangeException(nameof(audioEntry), Invariant(
                $"Only Audio entries are supported, but an entry of {audioEntry.Type} type was provides."));
        if (audioEntry.Content.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(audioEntry), Invariant(
                $"{nameof(audioEntry)} doesn't have Content."));

        return urlMapper.ToAbsolute(urlMapper.ApiBaseUrl, "audio/download/" + audioEntry.Content);
    }
}
