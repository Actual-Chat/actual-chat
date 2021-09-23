using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl;
using Stl.Async;

namespace ActualChat.Channels
{
    public static class ChannelExt
    {
        internal static readonly ChannelClosedException ChannelClosedError = new();

        public static ChannelCopier<T> CreateCopier<T>(this Channel<T> source)
            => new(source);
        public static ChannelCopier<T> CreateCopier<T>(this ChannelReader<T> source)
            => new(source);

        public static async ValueTask<Option<T>> TryReadAsync<T>(
            this ChannelReader<T> channel,
            CancellationToken cancellationToken = default)
        {
            while (await channel.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (channel.TryRead(out var value))
                return value;
            return Option<T>.None;
        }

        public static async ValueTask<Result<T>> ReadResultAsync<T>(
            this ChannelReader<T> channel,
            CancellationToken cancellationToken = default)
        {
            try {
                while (await channel.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                while (channel.TryRead(out var value)) {
                    return value;
                }
                return GetChannelClosedResult<T>();
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception e) {
                return Result.New<T>(default!, e);
            }
        }

        public static async ValueTask WriteResultAsync<T>(
            this ChannelWriter<T> channel,
            Result<T> result,
            CancellationToken cancellationToken = default)
        {
            if (result.IsValue(out var value))
                await channel.WriteAsync(value, cancellationToken).ConfigureAwait(false);
            else {
                var error = result.Error;
                if (error is ChannelClosedException)
                    channel.TryComplete();
                else
                    channel.TryComplete(error);
            }
        }

        public static Result<T> GetChannelClosedResult<T>()
            => Result.New<T>(default!, ChannelClosedError);
    }
}
