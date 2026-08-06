using ChannelExtCore = ActualLab.Channels.ChannelExt;

namespace ActualChat;

/// <summary>
/// Extension methods and factory methods for <see cref="Channel{T}"/>.
/// </summary>
public static partial class ChannelExt
{
    private static readonly ChannelClosedException ChannelClosedError = new();

    public static readonly UnboundedChannelOptions UnboundedPipeOptions = new() {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false,
    };
    public static readonly UnboundedChannelOptions UnboundedFanInOptions = new() {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    };
    public static readonly UnboundedChannelOptions UnboundedFanOutOptions = new() {
        SingleReader = false,
        SingleWriter = true,
        AllowSynchronousContinuations = false,
    };

    // Create

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Channel<T> Create<T>(ChannelOptions options)
        => ChannelExtCore.Create<T>(options);

    public static Channel<T> Create<T>(
        int? capacity,
        bool singleReader = false,
        bool singleWriter = false,
        bool allowSynchronousContinuations = true)
        => ChannelExtCore.Create<T>(capacity.HasValue
            ? new BoundedChannelOptions(capacity.Value) {
                SingleReader = singleReader,
                SingleWriter = singleWriter,
                AllowSynchronousContinuations = allowSynchronousContinuations,
                FullMode = BoundedChannelFullMode.Wait,
            }
            : new UnboundedChannelOptions {
                SingleReader = singleReader,
                SingleWriter = singleWriter,
                AllowSynchronousContinuations = allowSynchronousContinuations,
            });

    // Memoize

    public static AsyncMemoizer<T> Memoize<T>(
        this Channel<T> source,
        CancellationToken cancellationToken = default)
        => new(source.Reader.ReadAllAsync(cancellationToken), int.MaxValue, cancellationToken);

    public static AsyncMemoizer<T> Memoize<T>(
        this Channel<T> source,
        int capacity,
        CancellationToken cancellationToken = default)
        => new(source.Reader.ReadAllAsync(cancellationToken), capacity, cancellationToken);

    public static AsyncMemoizer<T> Memoize<T>(
        this ChannelReader<T> source,
        CancellationToken cancellationToken = default)
        => new(source.ReadAllAsync(cancellationToken), int.MaxValue, cancellationToken);

    public static AsyncMemoizer<T> Memoize<T>(
        this ChannelReader<T> source,
        int capacity,
        CancellationToken cancellationToken = default)
        => new(source.ReadAllAsync(cancellationToken), capacity, cancellationToken);

    // ReadOrNone & other helpers

    public static async Task<Option<T>> ReadOrNone<T>(
        this ChannelReader<T> channel,
        CancellationToken cancellationToken = default)
    {
        while (await channel.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            if (channel.TryRead(out var value)) // Technically it should always pass the "if" here
                return value;

        return Option<T>.None;
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

    public static async Task<bool> WaitToReadAndConsumeAsync<T>(
        this ChannelReader<T> reader,
        CancellationToken cancellationToken = default)
    {
#pragma warning disable CA1849
        var canRead = await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
        if (canRead)
            while (reader.TryRead(out _)) { }
        return canRead;
#pragma warning restore CA1849
    }
}
