namespace ActualChat.Media;

public sealed record CrawledLink(
    MediaId PreviewMediaId,
    OpenGraph OpenGraph
) {
    public static readonly CrawledLink None = new (MediaId.None, OpenGraph.None);
}
