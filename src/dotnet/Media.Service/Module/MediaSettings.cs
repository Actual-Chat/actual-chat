using System.Collections.ObjectModel;

namespace ActualChat.Media.Module;

public sealed class MediaSettings
{
    public TimeSpan CrawlTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan GraphParseTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ImageDownloadTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public IReadOnlySet<string> DomainsWithoutRobots { get; set; } = ReadOnlySet<string>.Empty;
    public string GithubApiKey { get; set; } = "";
    public TimeSpan LinkPreviewUpdatePeriod { get; set; } = TimeSpan.FromDays(1);
    // A crawl that misses the thumbnail retries with exponential backoff between these two delays,
    // stretched to the Retry-After the image host asked for (#4884)
    public TimeSpan LinkPreviewRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan LinkPreviewMaxRetryDelay { get; set; } = TimeSpan.FromMinutes(30);
    public int LinkPreviewRetryCount { get; set; } = 8;
    public string KlipyApiKey { get; set; } = "";
    // A suggestion nobody accepted or dismissed holds a ~50KB blob; the sweep collects it.
    public TimeSpan ImageSuggestionLifespan { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan ImageSuggestionSweepInterval { get; set; } = TimeSpan.FromHours(6);
    public int MaxConcurrentImageGenerations { get; set; } = 8;
}
