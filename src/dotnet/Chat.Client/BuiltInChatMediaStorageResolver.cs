namespace ActualChat.Chat.Client;

public class BuiltInChatMediaStorageResolver : IChatMediaStorageResolver
{
    private readonly UriMapper _uriMapper;

    public BuiltInChatMediaStorageResolver(UriMapper uriMapper)
        => _uriMapper = uriMapper;

    public Uri GetAudioBlobAddress(ChatEntry audioEntry)
    {
        if (audioEntry.ContentType != ChatContentType.Audio)
            throw new InvalidOperationException(Invariant(
                $"Only Audio chat entries supported, but {nameof(audioEntry)} has Id: {audioEntry.Id}, ContentType: {audioEntry.ContentType} "));

        if (audioEntry.Content.IsNullOrEmpty())
            throw new InvalidOperationException(Invariant(
                $"{nameof(audioEntry)} doesn't have BlobId at {nameof(audioEntry.Content)}"));

        return _uriMapper.ToAbsolute("/api/audio/download/" + audioEntry.Content);
    }

    public Uri GetVideoBlobAddress(ChatEntry videoChatEntry)
#pragma warning disable MA0025
        => throw new NotImplementedException();
#pragma warning restore MA0025
}
