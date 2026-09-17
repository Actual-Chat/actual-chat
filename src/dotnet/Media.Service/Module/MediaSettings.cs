using System.Collections.ObjectModel;

namespace ActualChat.Media.Module;

public sealed class MediaSettings
{
    public TimeSpan CrawlTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan GraphParseTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ImageDownloadTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public IReadOnlySet<string> DomainsWithoutRobots { get; set; } = ReadOnlySet<string>.Empty;
    public string GithubApiKey { get; set; } = "";
    public string[] CrawlingCidrDenylist { get; set; } = [];
    public string[] CrawlingDomainDenylist { get; set; } = [];
    public string[] CrawlingHostAllowList { get; set; } = [];
    public TimeSpan LinkPreviewUpdatePeriod { get; set; } = TimeSpan.FromDays(1);
    public string KlipyApiKey { get; set; } = "";
    // A suggestion nobody accepted or dismissed holds a ~50KB blob; the sweep collects it.
    public TimeSpan ImageSuggestionLifespan { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan ImageSuggestionSweepInterval { get; set; } = TimeSpan.FromHours(6);
    public int MaxConcurrentImageGenerations { get; set; } = 8;
}
