namespace ActualChat.ContentCaching;

public sealed record ContentRequest(Uri Url)
{
    public HttpMethod Method { get; init; } = HttpMethod.Get;
    public IReadOnlyDictionary<string, string> Headers { get; init; } = ImmutableDictionary<string, string>.Empty;
    // A cold range joins or starts the fill of the whole resource and is read from it, instead of
    // being fetched and stored on its own - for a caller reading one resource in many small ranges
    public bool MustFillWhole { get; init; }
}
