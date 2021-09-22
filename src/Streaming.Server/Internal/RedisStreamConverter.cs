using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Stl.Async;

namespace ActualChat.Streaming.Server.Internal
{
    public class RedisStreamConverter<TStreamId, TPart>
        where TStreamId : notnull
    {
        protected RedisStreamingOptionsBase<TStreamId, TPart> Setup { get; init; }
        protected IDatabase Database { get; init; }
        protected IConnectionMultiplexer Redis => Database.Multiplexer;
        protected ILogger Log { get; init; }

        public RedisStreamConverter(
            RedisStreamingOptionsBase<TStreamId, TPart> setup,
            IDatabase database,
            ILogger log)
        {
            Setup = setup;
            Database = database;
            Log = log;
        }

        public async Task Convert(
            TStreamId streamId,
            ChannelWriter<TPart> writer,
            CancellationToken cancellationToken)
        {
            var recordKey = Setup.StreamKeyProvider(streamId);

            Exception? error = null;
            var position = (RedisValue) "0-0";
            try {
                while (true) {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entries = await Database.StreamReadAsync(recordKey, position, 10);
                    if (entries?.Length > 0)
                        foreach (var entry in entries) {
                            var status = entry[Setup.StatusKey];
                            var isCompleted = status != RedisValue.Null && status == Setup.CompletedStatus;
                            if (isCompleted)
                                return;

                            var serializedPart = (ReadOnlyMemory<byte>) entry[Setup.PartKey];
                            var part = MessagePackSerializer.Deserialize<TPart>(
                                serializedPart, MessagePackSerializerOptions.Standard, cancellationToken);
                            await writer.WriteAsync(part, cancellationToken);
                            position = entry.Id;
                        }
                    else {
                        var hasNewMessageOpt = await WaitForNewMessage(streamId, cancellationToken)
                            .WithTimeout(Setup.WaitForNewMessageTimeout, cancellationToken);
                        if (!hasNewMessageOpt.IsSome(out var hasNewMessage))
                            throw new TimeoutException("Timout while trying to fetch a new message.");
                        if (!hasNewMessage)
                            return;
                    }
                }
            }
            catch (Exception ex) {
                error = ex;
                Log.LogError(error, "Redis stream conversion error");
            }
            finally {
                writer.Complete(error);
            }
        }

        public async Task<bool> WaitForNewMessage(
            TStreamId streamId,
            CancellationToken cancellationToken)
        {
            ISubscriber? subscriber = null;
            try {
                subscriber = Redis.GetSubscriber();
                var queue = await subscriber.SubscribeAsync(Setup.NewPartNewsChannelKeyProvider(streamId));
                await queue.ReadAsync(cancellationToken);
                return true;
            }
            catch (ChannelClosedException) {
                // Intended
            }
            finally {
                try {
                    if (subscriber != null)
                        await subscriber.UnsubscribeAsync(Setup.NewPartNewsChannelKeyProvider(streamId));
                }
                catch {
                    // Intended
                }
            }
            return false;
        }
    }
}
