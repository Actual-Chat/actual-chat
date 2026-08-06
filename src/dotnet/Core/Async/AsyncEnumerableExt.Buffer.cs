namespace ActualChat;

public static partial class AsyncEnumerableExt
{
    public static async IAsyncEnumerable<TSource> Buffer<TSource>(
        this IAsyncEnumerable<TSource> source,
        int count,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var buffer = new Queue<TSource>(count);
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            buffer.Enqueue(item);
            while (buffer.Count >= count) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return buffer.Dequeue();
            }
        }

        while (buffer.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            yield return buffer.Dequeue();
        }
    }

    public static async IAsyncEnumerable<List<TSource>> Buffer<TSource>(
        this IAsyncEnumerable<TSource> source,
        TimeSpan bufferDuration,
        MomentClock clock,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        // I don't care much about optimization with two value tasks here
        // (MoveNextAsync and PeriodicTimer.WaitForNextTickAsync) as we still create new lists
        bufferDuration = bufferDuration.Positive();
        var buffer = new List<TSource>();
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        var delayTask = clock.Delay(bufferDuration, cancellationToken);
        while (true) {
            var moveNextValueTask = enumerator.MoveNextAsync();
            if (buffer.Count == 0) {
                if (await moveNextValueTask.ConfigureAwait(false)) {
                    buffer.Add(enumerator.Current);
                    if (delayTask.IsCompleted) {
                        // Keep this block - it's not a duplicate of the similar one below - because we want to await the value task once
                        // The delay has completed, so we yield the current buffer.
                        yield return buffer;

                        buffer = new List<TSource>();
                        delayTask = clock.Delay(bufferDuration, cancellationToken);
                    }
                    continue;
                }
                yield break;
            }

            var moveNextTask = moveNextValueTask.AsTask();
            var completedTask = await Task.WhenAny(moveNextTask, delayTask).ConfigureAwait(false);
            if (completedTask == delayTask) {
                yield return buffer;

                await delayTask.ConfigureAwait(false); // Propagate any exceptions from the delay.
                buffer = new List<TSource>(); // Start a new buffer.
                delayTask = clock.Delay(bufferDuration, cancellationToken); // Restart the delay.

                // Now we must wait for the pending moveNextTask to complete.
                if (await moveNextTask.ConfigureAwait(false))
                    buffer.Add(enumerator.Current);
                else
                    yield break;
            }
            else {
                if (await moveNextTask.ConfigureAwait(false)) // Propagate any exceptions from the source.
                    buffer.Add(enumerator.Current);
                else {
                    yield return buffer;

                    yield break;
                }
            }
        }
    }
}
