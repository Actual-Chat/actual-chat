namespace ActualChat;

public sealed class UriMapper
{
    public Uri BaseUri { get; }

    public UriMapper(string baseUri) : this(new Uri(baseUri)) { }
    public UriMapper(Uri baseUri) => BaseUri = baseUri;

    public Uri ToAbsolute(string relativeUri)
        => new(BaseUri, relativeUri);

    public virtual Uri GetChatUrl(string chatId, long? entryId = null)
        => entryId.HasValue
            ? new Uri(BaseUri, $"/chat/{chatId}#{entryId}")
            : new Uri(BaseUri, $"/chat/{chatId}");
}
