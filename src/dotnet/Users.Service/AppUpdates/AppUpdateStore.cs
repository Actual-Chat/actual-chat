using ActualChat.Redis;
using ActualChat.Users.Db;
using ActualLab.Redis;

namespace ActualChat.Users;

/// <summary>
/// The Redis side of app-update detection: one record per app kind, no TTL, plus the
/// throttle key that keeps the cluster from probing in lockstep.
/// </summary>
public sealed class AppUpdateStore(IServiceProvider services)
{
    private RedisDb RedisDb { get; }
        = services.GetRequiredService<RedisDb<UsersDbContext>>().WithKeyPrefix("AppUpdates");

    public Task<AppUpdateRecord?> Get(AppKind appKind, CancellationToken cancellationToken)
        => RedisDb.Get<AppUpdateRecord>(Key(appKind), cancellationToken);

    public Task Set(AppKind appKind, AppUpdateRecord record, CancellationToken cancellationToken)
        => RedisDb.Set(Key(appKind), record, cancellationToken);

    public Task<bool> TryStartProbe(AppKind appKind, TimeSpan window, CancellationToken cancellationToken)
        // False means another node probed this kind within the window, so this turn can be skipped
        => RedisDb.TrySetOnce($"probe:{Key(appKind)}", window, cancellationToken);

    // Protected/internal methods

    // It's internal to be accessible from tests
    internal async Task Remove(AppKind appKind, CancellationToken cancellationToken)
    {
        // The throttle key goes too: it outlives a test by MinProbeInterval, and that would
        // keep the next test's probe from running at all.
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await database.KeyDeleteAsync(Key(appKind)).ConfigureAwait(false);
        await database.KeyDeleteAsync($"probe:{Key(appKind)}").ConfigureAwait(false);
    }

    // Private methods

    private static string Key(AppKind appKind)
        => appKind.ToString();
}
