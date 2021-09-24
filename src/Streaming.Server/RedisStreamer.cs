using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Serialization;
using StackExchange.Redis;
using Stl.Async;

namespace ActualChat.Streaming.Server
{
    public class RedisStreamer<T>
    {
        public record Options
        {
            public string AppendPubSubKeySuffix { get; init; } = "-updates";
            public string ItemKey { get; init; } = "v";
            public string StatusKey { get; init; } = "s";
            public string CompletedStatus { get; init; } = "c";
            public TimeSpan ReadItemTimeout { get; init; } = TimeSpan.FromSeconds(0.250);
            public ByteSerializer<T> Serializer { get; init; } = ByteSerializer<T>.Default;
        }

        public Options Setup { get; }
        public IDatabase Database { get; }
        public string Key { get; }
        public RedisPubSub AppendPubSub { get; }

        public RedisStreamer(Options setup, IDatabase database, string key)
        {
            Setup = setup;
            Database = database;
            Key = key;
            AppendPubSub = new RedisPubSub(database, $"{typeof(T).Name}-{Key}{Setup.AppendPubSubKeySuffix}");
        }

        public async Task Read(ChannelWriter<T> target, CancellationToken cancellationToken = default)
        {
            Exception? error = null;
            var position = (RedisValue) "0-0";
            try {
                while (true) {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entries = await Database.StreamReadAsync(Key, position, 10).ConfigureAwait(false);
                    if (entries?.Length > 0)
                        foreach (var entry in entries) {
                            var status = entry[Setup.StatusKey];
                            var isCompleted = status != RedisValue.Null && status == Setup.CompletedStatus;
                            if (isCompleted)
                                return;

                            var serialized = (ReadOnlyMemory<byte>) entry[Setup.ItemKey];
                            var item = Setup.Serializer.Deserialize(serialized);
                            await target.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                            position = entry.Id;
                        }
                    else {
                        var appendOpt = await AppendPubSub.Fetch(cancellationToken)
                            .WithTimeout(Setup.ReadItemTimeout, cancellationToken)
                            .ConfigureAwait(false);
                        var (hasValue, fetch) = appendOpt;
                        if (hasValue && fetch.IsNull)
                            return;
                    }
                }
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
            ChannelReader<T> stream,
            CancellationToken cancellationToken = default)
            => Write(stream, _ => ValueTask.CompletedTask, cancellationToken);

        public async Task Write(
            ChannelReader<T> stream,
            Func<RedisStreamer<T>, ValueTask> newStreamNotifier,
            CancellationToken cancellationToken = default)
        {
            var isStreaming = false;
            while (await stream.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) {
                var mustNotify = false;
                try {
                    while (stream.TryRead(out var item)) {
                        await AppendItem(item, false).ConfigureAwait(false);
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
            
            await Database.StreamAddAsync(
                Key, Setup.StatusKey, Setup.CompletedStatus,
                maxLength: 1000, useApproximateMaxLength: true).ConfigureAwait(false);
        }

        public async Task AppendItem(T item, bool notify = true)
        {
            using var writer = Setup.Serializer.Serialize(item);
            await Database.StreamAddAsync(Key, Setup.ItemKey, writer.WrittenMemory,
                maxLength: 1000, useApproximateMaxLength: true)
                .ConfigureAwait(false);
            if (notify)
                await AppendPubSub.Publish(RedisValue.EmptyString).ConfigureAwait(false);
        }

        public Task Remove()
            => Database.KeyDeleteAsync(Key, CommandFlags.FireAndForget);
    }
}
