namespace ActualChat;

public static partial class AsyncEnumerableExt
{
    public static (IAsyncEnumerable<TSource> Matched, IAsyncEnumerable<TSource> NotMatched) Split<TSource>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource, bool> splitPredicate,
        CancellationToken cancellationToken = default)
        => source.Split((_, s) => splitPredicate(s), cancellationToken);

    public static (IAsyncEnumerable<TSource> Matched, IAsyncEnumerable<TSource> NotMatched) Split<TSource>(
        this IAsyncEnumerable<TSource> source,
        Func<int, TSource, bool> splitPredicate,
        CancellationToken cancellationToken = default)
    {
        var matched = Channel.CreateUnbounded<TSource>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });
        var notMatched = Channel.CreateUnbounded<TSource>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });

        _ = BackgroundTask.Run(async () => {
                Exception? error = null;
                try {
                    var i = 0;
                    await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                        if (splitPredicate(i++, item))
                            await matched.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                        else
                            await notMatched.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) {
                    error = e;
                }
                finally {
                    matched.Writer.TryComplete(error);
                    notMatched.Writer.TryComplete(error);
                }
            },
            cancellationToken);

        return (matched.Reader.ReadAllAsync(cancellationToken), notMatched.Reader.ReadAllAsync(cancellationToken));
    }

    public static (Task<TSource> HeadTask, IAsyncEnumerable<TSource> Tail) SplitHead<TSource>(
        this IAsyncEnumerable<TSource> source,
        CancellationToken cancellationToken = default)
    {
        var headSource = TaskCompletionSourceExt.New<TSource>();
        var notMatched = Channel.CreateUnbounded<TSource>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });

        _ = BackgroundTask.Run(async () => {
                Exception? error = null;
                try {
                    bool isHead = true;
                    await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                        if (isHead) {
                            isHead = false;
                            headSource.SetResult(item);
                        }
                        else
                            await notMatched.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) {
                    error = e;
                }
                finally {
                    if (error != null)
                        headSource.TrySetException(error);
                    else
                        headSource.TrySetCanceled();
                    notMatched.Writer.TryComplete(error);
                }
            },
            cancellationToken);

        return (headSource.Task, notMatched.Reader.ReadAllAsync(cancellationToken));
    }
}
