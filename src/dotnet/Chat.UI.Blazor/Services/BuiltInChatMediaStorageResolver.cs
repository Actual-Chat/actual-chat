namespace ActualChat.Chat.UI.Blazor.Services;

public class BuiltInChatMediaResolver : IChatMediaResolver
{
    private readonly UriMapper _uriMapper;

    public BuiltInChatMediaResolver(UriMapper uriMapper)
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
#pragma warning disable MA0025
        => throw new NotImplementedException();
#pragma warning restore MA0025
}
