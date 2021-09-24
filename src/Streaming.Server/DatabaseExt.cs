using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using StackExchange.Redis;
using Stl.Async;

namespace ActualChat.Streaming.Server
{
    public static class DatabaseExt
    {
        public static async Task ReadStream<T>(
            this IDatabase database,
            string streamKey,
            ChannelWriter<T> channel,
            RedisChannelOptions<T> options,
            CancellationToken cancellationToken = default)
        {
            var newItemNewsKey = streamKey + options.NewItemNotifyKeySuffix;
            Exception? error = null;
            var position = (RedisValue) "0-0";
            try {
                while (true) {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entries = await database.StreamReadAsync(streamKey, position, 10);
                    if (entries?.Length > 0)
                        foreach (var entry in entries) {
                            var status = entry[options.StatusKey];
                            var isCompleted = status != RedisValue.Null && status == options.CompletedStatus;
                            if (isCompleted)
                                return;

                            var serializedItem = (ReadOnlyMemory<byte>) entry[options.PartKey];
                            var item = options.Deserializer.Invoke(serializedItem);
                            await channel.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                            position = entry.Id;
                        }
                    else {
                        var hasNewMessageOpt = await WaitForNewMessage()
                            .WithTimeout(options.WaitForNewMessageTimeout, cancellationToken);
                        if (!hasNewMessageOpt.IsSome(out var hasNewMessage))
                            throw new TimeoutException("Timeout while trying to fetch a new message.");
                        if (!hasNewMessage)
                            return;
                    }
                }
            }
            catch (Exception e) {
                error = e;
            }
            finally {
                channel.Complete(error);
            }

            async Task<bool> WaitForNewMessage()
            {
                ISubscriber? subscriber = null;
                try {
                    subscriber = database.Multiplexer.GetSubscriber();
                    var newPartNews = await subscriber.SubscribeAsync(newItemNewsKey);
                    await newPartNews.ReadAsync(cancellationToken);
                    return true;
                }
                catch (ChannelClosedException) {
                    // Intended
                }
                finally {
                    try {
                        if (subscriber != null)
                            await subscriber.UnsubscribeAsync(newItemNewsKey);
                    }
                    catch {
                        // Intended
                    }
                }
                return false;
            }
        }
    }
}
