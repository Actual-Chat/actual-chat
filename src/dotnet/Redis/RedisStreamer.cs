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
        public string FailedStatus { get; init; } = "e";
        public TimeSpan ReadItemTimeout { get; init; } = TimeSpan.FromSeconds(0.250);
        public IByteSerializer<T> Serializer { get; init; } = ByteSerializer<T>.Default;
    }

    public Options Settings { get; }
    public RedisDb RedisDb { get; }
    public string Key { get; }
    public ILogger? Log { get; set; }

    public RedisStreamer(RedisDb redisDb, string key, Options? settings = null)
    {
        Settings = settings ?? new ();
        RedisDb = redisDb;
        Key = key;
    }

    public async IAsyncEnumerable<T> Read([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var appendPubSub = GetPubSub();
        await using var _ = appendPubSub.ConfigureAwait(false);

        var position = (RedisValue)"0-0";
        var attemptCount = 0;
        var serializer = Settings.Serializer;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested(); // Redis doesn't support cancellation
            Log?.LogDebug("RedisStreamer: [+] StreamReadAsync");
            var entries = await RedisDb.Database.StreamReadAsync(Key, position, 10).ConfigureAwait(false);
            Log?.LogDebug("RedisStreamer: [-] StreamReadAsync -> {EntryCount} entries", entries.Length);
            if (entries?.Length > 0)
                foreach (var entry in entries) {
                    var status = entry[Settings.StatusKey];
                    if (status == Settings.CompletedStatus)
                        yield break;
                    if (status == Settings.FailedStatus)
                        throw new InvalidOperationException("Source stream was completed with an error.");
                    var data = (ReadOnlyMemory<byte>)entry[Settings.ItemKey];
                    var item = serializer.Reader.Read(data);
                    yield return item;

                    position = entry.Id;
                }
            else {
                attemptCount++;
                try {
                    Log?.LogDebug("RedisStreamer: [+] appendPubSub.Read");
                    await appendPubSub.Read(cancellationToken)
                        .AsTask()
                        .WithTimeout(Settings.ReadItemTimeout, cancellationToken)
                        .ConfigureAwait(false);
                    Log?.LogDebug("RedisStreamer: [-] appendPubSub.Read");
                }
                catch (ChannelClosedException) {
                    Log?.LogDebug("RedisStreamer: [-] appendPubSub.Read -> stream completed");
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
        Func<RedisStreamer<T>, ValueTask> newStreamAnnouncer,
        CancellationToken cancellationToken = default)
    {
        var appendPubSub = GetPubSub();
        await using var _ = appendPubSub.ConfigureAwait(false);

        var isAnnounced = false;
        Task? lastAppendTask = null;
        var finalStatus = Settings.FailedStatus;
        try {
            await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (lastAppendTask != null)
                    await lastAppendTask.ConfigureAwait(false);
                lastAppendTask = Append(item);
            }
            if (lastAppendTask != null)
                await lastAppendTask.ConfigureAwait(false);
            finalStatus = Settings.CompletedStatus;
        }
        finally {
            if (lastAppendTask != null) {
                try {
                    await lastAppendTask.ConfigureAwait(false);
                }
                catch {
                    finalStatus = Settings.FailedStatus;
                }
                if (isAnnounced)
                    await RedisDb.Database.StreamAddAsync(
                            Key, Settings.StatusKey, finalStatus,
                            maxLength: 1000,
                            useApproximateMaxLength: true)
                        .ConfigureAwait(false);
            }
        }

        async Task Append(T item)
        {
            Log?.LogDebug("RedisStreamer: [+] Append");
            await AppendItem(item, appendPubSub, cancellationToken).ConfigureAwait(false);
            Log?.LogDebug("RedisStreamer: [-] Append");
            if (isAnnounced)
                return;
            isAnnounced = true;
            Log?.LogDebug("RedisStreamer: [+] newStreamAnnouncer.Invoke");
            await newStreamAnnouncer.Invoke(this).ConfigureAwait(false);
            Log?.LogDebug("RedisStreamer: [-] newStreamAnnouncer.Invoke");
        }
    }

    public async Task AppendItem(T item, RedisPubSub? appendPubSub, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); // StackExchange.Redis doesn't support cancellation
        using var bufferWriter = Settings.Serializer.Writer.Write(item);
        await RedisDb.Database.StreamAddAsync(
                Key,
                Settings.ItemKey,
                bufferWriter.WrittenMemory,
                maxLength: 1000,
                useApproximateMaxLength: true)
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
