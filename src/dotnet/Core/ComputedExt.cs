using ActualLab.Fusion.Internal;
using ActualLab.Fusion.Operations.Internal;

namespace ActualChat;

public static class ComputedExt
{
    public static readonly TimeSpan MinSafeInvalidationDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxSafeInvalidationDelay = TimeSpan.FromMinutes(30);

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo?> FieldCache = new();

    public static void InvalidateSafely(this Computed? computed,
        TimeSpan delay,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
        => computed.InvalidateSafely(delay, MaxSafeInvalidationDelay, file, member, line);

    public static void InvalidateSafely(this Computed? computed,
        TimeSpan delay,
        TimeSpan maxDelay,
        [CallerFilePath] string? file = null,
        [CallerMemberName] string? member = null,
        [CallerLineNumber] int line = 0)
    {
        // A pending invalidation pins its Computed<T> until it fires, so a far-future delay retains
        // it for that long - and the cost scales with the method's parameter cardinality. Clamping
        // only makes the method recompute (and re-arm) earlier; it never changes the result.
        // The lower bound matters too: a delay of 0 invalidates immediately, so an already-past
        // expiration would spin.
        if (computed is null)
            return;

        delay = delay.Clamp(MinSafeInvalidationDelay, maxDelay);
        computed.Invalidate(delay, new InvalidationSource(file, member, line));
    }

    public static IState<T>? GetState<T>(this Computed<T> computed)
    {
        var untypedState = (computed as IStateBoundComputed)?.State;
        // If untypedState is not null, it can only be IState<T>, coz we've got Computed<T>
        return Unsafe.As<IState<T>?>(untypedState);
    }

    public static T UseIfReady<T>(this Task<T> task, T pendingValue, Computed computed)
    {
        // Lets a computation depend on something it must not wait for - typically a
        // RemoteComputedCacheMode.NoCache method, which never completes while offline. The result is
        // used only if it's already there; otherwise `pendingValue` stands in and `computed` is
        // invalidated once the real value arrives, so the computation re-runs and picks it up.
        if (task.IsCompletedSuccessfully)
            return task.Result;

        _ = task.ContinueWith(static (t, state) => {
                if (t.IsCompletedSuccessfully)
                    ((Computed)state!).Invalidate();
                else
                    _ = t.Exception; // Observe it, otherwise it surfaces as UnobservedTaskException
            },
            computed,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return pendingValue;
    }

    public static string DebugDump(this Computed computed, int maxDepth = 0)
    {
        var type = computed.GetType();
        var fState = GetField(type, "_state")!;

        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        // var flags = pFlags.GetGetter<ComputedFlags>().Invoke(computed);
        var invalidationFlags = (InvalidationFlags)((int)fState.GetValue(computed)! & 0xF00);
        sb.Append("Computed: ").Append(computed).AppendLine();
        sb.Append("- Invalidation flags: ").Append(invalidationFlags).AppendLine();
        DumpDependencies(ComputedImpl.GetDependencies(computed), 0);
        return sb.ToStringAndRelease();

        void DumpDependencies(Computed[] dependencies, int depth)
        {
            if (dependencies.Length == 0)
                return;

            if (depth > maxDepth)
                return;

            sb.Append(' ', depth * 2).AppendLine("- Dependencies:");
            foreach (var d in dependencies.OrderBy(i => i.Input.HashCode)) {
                sb.Append(' ', (depth + 1) * 2).Append("- ").Append(d).AppendLine();
                DumpDependencies(ComputedImpl.GetDependencies(d), depth + 1);
            }
        }
    }

    // Private methods

    private static PropertyInfo? GetProperty(Type type, string name)
        => PropertyCache.GetOrAdd((type, name), static state => {
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
        => FieldCache.GetOrAdd((type, name), static state => {
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
