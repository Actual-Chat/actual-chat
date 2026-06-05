using ActualLab.Resilience;

namespace ActualChat;

public abstract class ResilientStream
{
    public static ChannelOptions DefaultChannelOptions { get; set; } = new BoundedChannelOptions(64) {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait,
    };

    public ChannelOptions ChannelOptions { get; init; } = DefaultChannelOptions;
    public IRetryPolicy RetryPolicy { get; init; } = new RetryPolicy(RetryDelaySeq.Exp(0.25, 5));
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}

/// <summary>
/// A reliability wrapper that retries the source async stream on transient failures,
/// presenting a seamless <see cref="IAsyncEnumerable{T}"/> to the consumer.
/// </summary>
public sealed class ResilientStream<T> : ResilientStream, IAsyncEnumerable<T>
{
    public required Func<CancellationToken, Task<IAsyncEnumerable<T>>> Provider { get; init; }
    public Option<T> ResetItem { get; init; }
    // When set, a normal completion of the source is treated as a transient drop and reconnected.
    public bool IsInfinite { get; init; }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.CanBeCanceled)
            cancellationToken = CancellationToken;
        var channel = GetChannel(cancellationToken);
        return channel.Reader.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    public Channel<T> GetChannel(CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.CanBeCanceled)
            cancellationToken = CancellationToken;
        var channel = ChannelExt.Create<T>(ChannelOptions);
        _ = Task.Run(() => PushItems(channel.Writer, cancellationToken), CancellationToken.None);
        return channel;
    }

    // Private methods

    private async Task PushItems(ChannelWriter<T> writer, CancellationToken cancellationToken)
    {
        var failedTryCount = 0;
        try {
            while (true) {
                try {
                    var source = await Provider.Invoke(cancellationToken).ConfigureAwait(false);
                    await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                        failedTryCount = 0;
                        await writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                    }
                    if (!IsInfinite) {
                        writer.TryComplete();
                        return;
                    }
                    // Infinite stream completed - reconnect just as we do on a transient drop
                    ++failedTryCount;
                }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    if (!RetryPolicy.MustRetry(e, ref failedTryCount, out _)) {
                        writer.TryComplete(e);
                        return;
                    }
                }

                if (ResetItem.IsSome(out var resetItem))
                    await writer.WriteAsync(resetItem, cancellationToken).ConfigureAwait(false);

                var delay = RetryPolicy.Delays[failedTryCount];
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            writer.TryComplete(e);
        }
    }
}
