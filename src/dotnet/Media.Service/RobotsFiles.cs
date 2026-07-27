using ActualLab.Diagnostics;
using ActualLab.Locking;
using TurnerSoftware.RobotsExclusionTools;

namespace ActualChat.Media;

public class RobotsFiles(
    IHttpClientFactory httpClientFactory,
    ILogger<Crawler> log)
{
    public const string HttpClientName = nameof(RobotsFiles);
    private readonly AsyncLockSet<string> _locks = new (LockReentryMode.Unchecked,
        AsyncLockSet<string>.DefaultConcurrencyLevel,
        AsyncLockSet<string>.DefaultCapacity,
        StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentLruCache<string, RobotsFile> _robotsByDomain =
        new (10_000, comparer: StringComparer.OrdinalIgnoreCase);

    private ILogger? DebugLog { get; } = log.IfEnabled(LogLevel.Debug, Constants.DebugMode.RobotsFiles);
    private RobotsFileParser RobotsParser => field ??= new (httpClientFactory.CreateClient(HttpClientName));

    public async Task<RobotsFile> Get(Uri uri, CancellationToken cancellationToken)
    {
        var host = uri.DnsSafeHost;
        if(_robotsByDomain.TryGetValue(host, out var robotsFile))
            return robotsFile;

        using var _1 = await _locks.Lock(host, cancellationToken).ConfigureAwait(false);
        if(_robotsByDomain.TryGetValue(host, out robotsFile))
            return robotsFile;

        var baseUri = uri.ToBase();
        robotsFile = await BackgroundTask
            .Run(async () => {
                try {
                    return await RobotsParser
                        .FromUriAsync(new (baseUri, "/robots.txt"), RobotsFileAccessRules.LikeGoogle, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception e) when (e.IsCancellationOf(cancellationToken)) {
                    log.LogWarning(e, "Failed to fetch robots for {BaseUri}", baseUri);
                    return RobotsFile.AllowAllRobots(baseUri);
                }
            }, cancellationToken)
            .ConfigureAwait(false);
        _robotsByDomain.TryAdd(host, robotsFile);
        DebugLog?.LogInformation("Retrieved RobotsFile for '{Host}'", baseUri);
        return robotsFile;
    }
}
