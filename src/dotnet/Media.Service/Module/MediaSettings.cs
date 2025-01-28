using System.Collections.ObjectModel;

namespace ActualChat.Media.Module;

public sealed class MediaSettings
{
    public TimeSpan LinkPreviewUpdatePeriod { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan GraphParseTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ImageDownloadTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public IReadOnlySet<string> DomainsWithoutRobots { get; set; } = ReadOnlySet<string>.Empty;
}
