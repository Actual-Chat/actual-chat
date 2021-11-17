namespace ActualChat;

public class UriMapper
{
    public Uri BaseUri { get; }

    public UriMapper(string baseUri) : this(new Uri(baseUri)) { }
    public UriMapper(Uri baseUri) => BaseUri = baseUri;

    public virtual Uri ToAbsolute(string relativeUri)
        => new(BaseUri, relativeUri);
}
