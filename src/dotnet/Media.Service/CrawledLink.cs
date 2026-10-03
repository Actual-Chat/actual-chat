namespace ActualChat.Media;

public sealed record CrawledLink(
    MediaId? PreviewMediaId,
    OpenGraph OpenGraph,
    TimeSpan? RetryDelay = null
) {
    public static readonly CrawledLink None = new(null, OpenGraph.None);
}
