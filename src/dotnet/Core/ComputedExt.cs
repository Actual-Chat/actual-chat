using Cysharp.Text;
using Stl.Fusion.Client.Interception;
using Stl.Fusion.Internal;
using Stl.Fusion.Operations.Internal;

namespace ActualChat;

public static class ComputedExt
{
    public static async ValueTask<Computed<T>> UpdateIfCached<T>(this Computed<T> computed,
        TimeSpan waitDuration,
        CancellationToken cancellationToken = default)
    {
        using var waitCts = new CancellationTokenSource(waitDuration);
        using var cts = waitCts.Token.LinkWith(cancellationToken);
        try {
            return await computed.UpdateIfCached(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (waitCts.Token.IsCancellationRequested) {
            // Timeout - we just update the computed in this case
            return await computed.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    public static async ValueTask<Computed<T>> UpdateIfCached<T>(this Computed<T> computed,
        CancellationToken cancellationToken = default)
    {
        while (true) {
            computed = await computed.Update(cancellationToken).ConfigureAwait(false);
            var clientComputed = computed as IClientComputed;
            if (clientComputed?.CacheEntry == null)
                break;

            if (clientComputed.Call is not { } call) {
                // No Call is bound yet - we just retry till the moment the call is bound
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!call.ResultTask.IsCompleted) {
                // Wait for either invalidation or call completion (which may trigger invalidation too)
                await Task.WhenAny(
                    computed.WhenInvalidated(cancellationToken),
                    call.ResultTask.WaitAsync(cancellationToken)
                ).ConfigureAwait(false);
                if (computed.IsInvalidated())
                    break; // Definitely not cached already
            }

            // call.ResultTask is completed, let's give system some time to process it
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        computed = await computed.Update(cancellationToken).ConfigureAwait(false);
        return computed;
    }

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
