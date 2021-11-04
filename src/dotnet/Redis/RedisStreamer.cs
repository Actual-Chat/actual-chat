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
        public Func<Channel<T>> ChannelFactory { get; init; } = DefaultChannelFactory;

        private static Channel<T> DefaultChannelFactory()
            => Channel.CreateBounded<T>(
                new BoundedChannelOptions(128) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true,
                });
    }

    public Options Settings { get; }
    public RedisDb RedisDb { get; }
    public string Key { get; }
    public RedisPubSub AppendPubSub { get; }

    public RedisStreamer(RedisDb redisDb, string key, Options? settings = null)
    {
        Settings = settings ?? new();
        RedisDb = redisDb;
        Key = key;
        AppendPubSub = RedisDb.GetPubSub(Key + Settings.AppendPubSubKeySuffix);
    }

    public ChannelReader<T> Read(CancellationToken cancellationToken = default)
    {
        var channel = Settings.ChannelFactory.Invoke();
        _ = Task.Run(() => Read(channel, cancellationToken), default);
        return channel;
    }

    public async Task Read(ChannelWriter<T> target, CancellationToken cancellationToken = default)
    {
        Exception? error = null;
        var position = (RedisValue) "0-0";
        var attemptCount = 0;
        var serializer = Settings.Serializer;
        try {
            while (true) {
                cancellationToken.ThrowIfCancellationRequested(); // Redis doesn't support cancellation
                var entries = await RedisDb.Database.StreamReadAsync(Key, position, 10).ConfigureAwait(false);
                if (entries?.Length > 0)
                    foreach (var entry in entries) {
                        if (entry[Settings.StatusKey] == Settings.CompletedStatus)
                            return;

                        var data = (ReadOnlyMemory<byte>) entry[Settings.ItemKey];
                        var item = serializer.Reader.Read(data);
                        await target.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                        position = entry.Id;
                    }
                else {
                    attemptCount++;
                    await AppendPubSub.Read(cancellationToken)
                        .AsTask().WithTimeout(Settings.ReadItemTimeout, cancellationToken)
                        .ConfigureAwait(false);
                    if (attemptCount > 10 && position == "0-0")
                        throw new TimeoutException($"RedisStreamer<T>.Read() exceeds the wait limit for empty stream with Key = {Key}.");
                }
            }
        }
        catch (ChannelClosedException) {
            // Not an error: AppendPubSub.Read is done reading (pub/sub is closed)
        }
        catch (Exception e) {
            error = e;
        }
        finally {
            target.TryComplete(error);
            await AppendPubSub.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task Write(
        ChannelReader<T> source,
        CancellationToken cancellationToken = default)
        => Write(source, _ => ValueTask.CompletedTask, cancellationToken);

    public async Task Write(
        ChannelReader<T> source,
        Func<RedisStreamer<T>, ValueTask> newStreamNotifier,
        CancellationToken cancellationToken = default)
    {
        var isStreaming = false;
        while (await source.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) {
            var mustNotify = false;
            try {
                while (source.TryRead(out var item)) {
                    await AppendItem(item, false, cancellationToken).ConfigureAwait(false);
                    mustNotify = true;
                }
            }
            finally {
                if (mustNotify)
                    await AppendPubSub.Publish(RedisValue.EmptyString).ConfigureAwait(false);
            }
            if (!isStreaming) {
                isStreaming = true;
                await newStreamNotifier.Invoke(this).ConfigureAwait(false);
            }
        }
        if (!isStreaming)
            await newStreamNotifier.Invoke(this).ConfigureAwait(false);

        await RedisDb.Database.StreamAddAsync(
            Key, Settings.StatusKey, Settings.CompletedStatus,
            maxLength: 1000, useApproximateMaxLength: true).ConfigureAwait(false);
    }

    public Task AppendItem(T item, CancellationToken cancellationToken = default)
        => AppendItem(item, true, cancellationToken);
    public async Task AppendItem(T item, bool notify, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); // StackExchange.Redis doesn't support cancellation
        using var bufferWriter = Settings.Serializer.Writer.Write(item);
        await RedisDb.Database.StreamAddAsync(
                Key, Settings.ItemKey, bufferWriter.WrittenMemory,
                maxLength: 1000, useApproximateMaxLength: true)
            .ConfigureAwait(false);
        if (notify)
            await AppendPubSub.Publish(RedisValue.EmptyString).ConfigureAwait(false);
    }

    public Task Remove()
        => RedisDb.Database.KeyDeleteAsync(Key, CommandFlags.FireAndForget);
}
