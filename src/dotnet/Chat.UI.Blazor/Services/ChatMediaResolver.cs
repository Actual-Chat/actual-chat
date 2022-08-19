namespace ActualChat.Chat.UI.Blazor.Services;

public interface IChatMediaResolver
{
    public Uri GetAudioBlobUri(ChatEntry audioEntry);
    public Uri GetVideoBlobUri(ChatEntry videoEntry);
}

public class ChatMediaResolver : IChatMediaResolver
{
    private readonly UriMapper _uriMapper;

    public ChatMediaResolver(UriMapper uriMapper)
        => _uriMapper = uriMapper;

    public Uri GetAudioBlobUri(ChatEntry audioEntry)
    {
        if (audioEntry.Type != ChatEntryType.Audio)
            throw new ArgumentOutOfRangeException(nameof(audioEntry), Invariant(
                $"Only Audio entries are supported, but an entry of {audioEntry.Type} type was provides."));
        if (audioEntry.Content.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(audioEntry), Invariant(
                $"{nameof(audioEntry)} doesn't have Content."));

        return _uriMapper.ToAbsolute("/api/audio/download/" + audioEntry.Content);
    }

    public Uri GetVideoBlobUri(ChatEntry videoEntry)
        => throw StandardError.NotSupported("Video isn't supported yet.");
}
