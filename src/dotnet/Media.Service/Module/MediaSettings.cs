namespace ActualChat.Media.Module;

public sealed class MediaSettings
{
    // DBs
    public string Db { get; set; } = "";
    public string Redis { get; set; } = "";
    public TimeSpan LinkPreviewUpdatePeriod { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan CrawlerGraphParsingTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan CrawlerImageDownloadTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan CrawlingTimeout => CrawlerGraphParsingTimeout + CrawlerImageDownloadTimeout + TimeSpan.FromSeconds(3);
}
