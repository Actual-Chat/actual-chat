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
            var streamKey = Setup.StreamKeyProvider(streamId);
            var newPartNewsChannelKey = Setup.NewPartNewsChannelKeyProvider(streamId);
            Log.LogInformation("Keys: stream = {StreamKey}, newPartNewsChannel = {NewPartNewsChannelKey}",
                streamKey, newPartNewsChannelKey);

            Exception? error = null;
            var position = (RedisValue) "0-0";
            try {
                while (true) {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entries = await Database.StreamReadAsync(streamKey, position, 10);
                    if (entries?.Length > 0)
                        foreach (var entry in entries) {
                            var status = entry[Setup.StatusKey];
                            var isCompleted = status != RedisValue.Null && status == Setup.CompletedStatus;
                            if (isCompleted)
                                return;

                            var serializedPart = (ReadOnlyMemory<byte>) entry[Setup.PartKey];
                            var part = MessagePackSerializer.Deserialize<TPart>(serializedPart);
                            await writer.WriteAsync(part, cancellationToken);
                            position = entry.Id;
                        }
                    else {
                        var hasNewMessageOpt = await WaitForNewMessage(newPartNewsChannelKey, cancellationToken)
                            .WithTimeout(Setup.WaitForNewMessageTimeout, cancellationToken);
                        if (!hasNewMessageOpt.IsSome(out var hasNewMessage))
                            throw new TimeoutException("Timeout while trying to fetch a new message.");
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

        private async Task<bool> WaitForNewMessage(
            string newPartNewsChannelKey,
            CancellationToken cancellationToken)
        {
            ISubscriber? subscriber = null;
            try {
                subscriber = Redis.GetSubscriber();
                var newPartNews = await subscriber.SubscribeAsync(newPartNewsChannelKey);
                await newPartNews.ReadAsync(cancellationToken);
                return true;
            }
            catch (ChannelClosedException) {
                // Intended
            }
            finally {
                try {
                    if (subscriber != null)
                        await subscriber.UnsubscribeAsync(newPartNewsChannelKey);
                }
                catch {
                    // Intended
                }
            }
            return false;
        }
    }
}
