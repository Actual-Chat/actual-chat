using Cysharp.Text;
using Stl.Fusion.Internal;
using Stl.Fusion.Operations.Internal;

namespace ActualChat;

public static class ComputedExt
{
    public static async Task<Computed<T>> When<T>(
        this ValueTask<Computed<T>> computedTask,
        Func<T, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        var computed = await computedTask.ConfigureAwait(false);
        return await computed.When(predicate, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Computed<T>> When<T>(
        this ValueTask<Computed<T>> computedTask,
        Func<T, bool> predicate,
        Timeout timeout,
        CancellationToken cancellationToken = default)
    {
        var computed = await computedTask.ConfigureAwait(false);
        return await computed.When(predicate, timeout, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Computed<T>> When<T>(
        this Computed<T> computed,
        Func<T, bool> predicate,
        Timeout timeout,
        CancellationToken cancellationToken = default)
    {
        var cts = cancellationToken.CreateLinkedTokenSource();
        try {
            var computedTask = computed.When(predicate, cts.Token);
            var timeoutTask = timeout.Wait(cts.Token);
            await Task.WhenAny(timeoutTask, computedTask).ConfigureAwait(false);
            if (timeoutTask.IsCompleted)
                throw new TimeoutException();

            return await computedTask.ConfigureAwait(false);
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    public static async IAsyncEnumerable<Computed<T>> Until<T>(
        this IAsyncEnumerable<Computed<T>> changes,
        Task task,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (task.IsCompleted) {
            await task.ConfigureAwait(false); // will throw exception in case of failed task
            yield break;
        }

        var enumerator = changes.GetAsyncEnumerator(cancellationToken);
        var hasNextChangeTask = enumerator.MoveNextAsync();
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.WhenAny(task, hasNextChangeTask.AsTask()).ConfigureAwait(false);

            if (task.IsCompleted) {
                await task.ConfigureAwait(false); // will throw exception in case of failed task
                yield break;
            }

            yield return enumerator.Current;
            hasNextChangeTask = enumerator.MoveNextAsync();
        }
    }

    // Debug dump

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> _propertyCache = new();
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo?> _fieldCache = new();

    public static string DebugDump(this IComputed computed)
    {
        var type = computed.GetType();
        var pFlags = GetProperty(type, "Flags")!;

        using var sb = ZString.CreateStringBuilder();
        // var flags = pFlags.GetGetter<ComputedFlags>().Invoke(computed);
        var flags = (ComputedFlags)pFlags.GetMethod!.Invoke(computed, Array.Empty<object?>())!;
        sb.Append("Computed: ");
        sb.AppendLine(computed.ToString()!);
        sb.Append("- Flags: ");
        sb.AppendLine(flags.ToString());
        sb.AppendLine("- Dependencies:");
        var impl = (IComputedImpl)computed;
        foreach (var d in impl.Used) {
            sb.Append("  - ");
            sb.AppendLine(d.ToString()!);
        }
        return sb.ToString();
    }

    private static PropertyInfo? GetProperty(Type type, string name)
        => _propertyCache.GetOrAdd((type, name), static state => {
            var (type1, name1) = state;
            while (type1 != null) {
                var result = type1.GetProperty(name1, BindingFlags.Instance | BindingFlags.NonPublic);
                if (result != null)
                    return result;

                type1 = type1.BaseType;
            }
            return null;
        });

    private static FieldInfo? GetField(Type type, string name)
        => _fieldCache.GetOrAdd((type, name), static state => {
            var (type1, name1) = state;
            while (type1 != null) {
                var result = type1.GetField(name1, BindingFlags.Instance | BindingFlags.NonPublic);
                if (result != null)
                    return result;

                type1 = type1.BaseType;
            }
            return null;
        });
}
