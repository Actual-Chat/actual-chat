namespace ActualChat;

public class UriMapper
{
    public virtual Uri BaseUri { get; }

    public UriMapper(Uri baseUri)
        => BaseUri = baseUri;

    public virtual Uri ToAbsolute(string relativeUri)
        => new(BaseUri, relativeUri);
}
