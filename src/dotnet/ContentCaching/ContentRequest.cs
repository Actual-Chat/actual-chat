namespace ActualChat.ContentCaching;

public sealed record ContentRequest(Uri Url)
{
    public HttpMethod Method { get; init; } = HttpMethod.Get;
    public IReadOnlyDictionary<string, string> Headers { get; init; } = ImmutableDictionary<string, string>.Empty;
    public string? ImmutableKey { get; init; }
}
