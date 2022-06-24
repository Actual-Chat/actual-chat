using Stl.Redis;

namespace ActualChat.Redis;

public static class RedisStreamerExt
{
    // TODO: Remove when Stl.Redis provides proper expiration handling
    public static async Task Write<T>(
        this RedisStreamer<T> streamer,
        IAsyncEnumerable<T> stream,
        TimeSpan endedStreamTtl,
        ILogger log,
        CancellationToken cancellationToken)
    {
        await using var _ = AsyncDisposable.New(KeyExpireSilently).ConfigureAwait(false);
        await streamer.Write(stream, cancellationToken).ConfigureAwait(false);

        async ValueTask KeyExpireSilently()
        {
            try
            {
                await streamer.RedisDb.Database.KeyExpireAsync(streamer.Key, endedStreamTtl).ConfigureAwait(false);
            }
            catch (Exception exc)
            {
                log.LogError(exc, "Could not set expiration for redis key='{RedisKey}'", streamer.Key);
            }
        }
    }
}
