using StackExchange.Redis;
using Stl.Redis;

namespace ActualChat.Redis;

public class RedisStreamer<T>
{
    public record Options
    {
        public string AppendPubSubKeySuffix { get; init; } = "-updates";
        public string ItemKey { get; init; } = "v";
        public string StatusKey { get; init; } = "s";
        public string CompletedStatus { get; init; } = "c";
        public TimeSpan ReadItemTimeout { get; init; } = TimeSpan.FromSeconds(0.250);
        public IByteSerializer<T> Serializer { get; init; } = ByteSerializer<T>.Default;
    }

    public Options Settings { get; }
    public RedisDb RedisDb { get; }
    public string Key { get; }

    public RedisStreamer(RedisDb redisDb, string key, Options? settings = null)
    {
        Settings = settings ?? new();
        RedisDb = redisDb;
        Key = key;
    }

    public async IAsyncEnumerable<T> Read([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var appendPubSub = GetPubSub();
        await using var _ = appendPubSub.ConfigureAwait(false);

        var position = (RedisValue) "0-0";
        var attemptCount = 0;
        var serializer = Settings.Serializer;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested(); // Redis doesn't support cancellation
            var entries = await RedisDb.Database.StreamReadAsync(Key, position, 10).ConfigureAwait(false);
            if (entries?.Length > 0)
                foreach (var entry in entries) {
                    if (entry[Settings.StatusKey] == Settings.CompletedStatus)
                        yield break;

                    var data = (ReadOnlyMemory<byte>) entry[Settings.ItemKey];
                    var item = serializer.Reader.Read(data);
                    yield return item;
                    position = entry.Id;
                }
            else {
                attemptCount++;
                try {
                    await appendPubSub.Read(cancellationToken)
                        .AsTask().WithTimeout(Settings.ReadItemTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (ChannelClosedException) {
                    yield break;
                }
                if (attemptCount > 10 && position == "0-0")
                    throw new TimeoutException(
                        $"RedisStreamer<T>.Read() exceeds the wait limit for empty stream with Key = {Key}.");
            }
        }
    }

    public Task Write(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
        => Write(source, _ => ValueTask.CompletedTask, cancellationToken);

    public async Task Write(
        IAsyncEnumerable<T> source,
        Func<RedisStreamer<T>, ValueTask> newStreamNotifier,
        CancellationToken cancellationToken = default)
    {
        var appendPubSub = GetPubSub();
        await using var _ = appendPubSub.ConfigureAwait(false);

        var isFirstItem = true;
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            await AppendItem(item, appendPubSub, cancellationToken).ConfigureAwait(false);
            if (isFirstItem) {
                isFirstItem = false;
                await newStreamNotifier.Invoke(this).ConfigureAwait(false);
            }
        }
        if (isFirstItem)
            await newStreamNotifier.Invoke(this).ConfigureAwait(false);

        await RedisDb.Database.StreamAddAsync(
            Key, Settings.StatusKey, Settings.CompletedStatus,
            maxLength: 1000, useApproximateMaxLength: true).ConfigureAwait(false);
    }

    public async Task AppendItem(T item, RedisPubSub? appendPubSub, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); // StackExchange.Redis doesn't support cancellation
        using var bufferWriter = Settings.Serializer.Writer.Write(item);
        await RedisDb.Database.StreamAddAsync(
                Key, Settings.ItemKey, bufferWriter.WrittenMemory,
                maxLength: 1000, useApproximateMaxLength: true)
            .ConfigureAwait(false);
        if (appendPubSub != null)
            await appendPubSub.Publish(RedisValue.EmptyString).ConfigureAwait(false);
    }

    public Task Remove()
        => RedisDb.Database.KeyDeleteAsync(Key, CommandFlags.FireAndForget);

    // Protected methods

    protected virtual RedisPubSub GetPubSub()
        => RedisDb.GetPubSub(Key + Settings.AppendPubSubKeySuffix);
}
