namespace ActualChat.Chat.UI.Blazor.Services;

public class BuiltInChatMediaResolver : IChatMediaResolver
{
    private readonly UriMapper _uriMapper;

    public BuiltInChatMediaResolver(UriMapper uriMapper)
        => _uriMapper = uriMapper;

    public Uri GetAudioBlobUri(ChatEntry audioEntry)
    {
        if (audioEntry.Type != ChatEntryType.Audio)
            throw new InvalidOperationException(Invariant(
                $"Only Audio chat entries supported, but {nameof(audioEntry)} has Id: {audioEntry.Id}, Type: {audioEntry.Type}."));

        if (audioEntry.Content.IsNullOrEmpty())
            throw new InvalidOperationException(Invariant(
                $"{nameof(audioEntry)} doesn't have BlobId at {nameof(audioEntry.Content)}"));

        return _uriMapper.ToAbsolute("/api/audio/download/" + audioEntry.Content);
    }

    public Uri GetVideoBlobUri(ChatEntry videoEntry)
#pragma warning disable MA0025
        => throw new NotImplementedException();
#pragma warning restore MA0025
}
