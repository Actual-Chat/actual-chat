namespace ActualChat;

public sealed class UriMapper
{
    public Uri BaseUri { get; }

    public UriMapper(string baseUri) : this(new Uri(baseUri)) { }
    public UriMapper(Uri baseUri) => BaseUri = baseUri;

    public Uri ToAbsolute(string relativeUri)
        => new(BaseUri, relativeUri);
}
