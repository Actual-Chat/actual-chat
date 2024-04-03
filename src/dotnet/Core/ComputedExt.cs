using Cysharp.Text;
using ActualLab.Fusion.Internal;
using ActualLab.Fusion.Operations.Internal;

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

    // Debug dump

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> _propertyCache = new();
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo?> _fieldCache = new();

    public static string DebugDump(this IComputed computed)
    {
        var type = computed.GetType();
        var pFlags = GetProperty(type, "Flags")!;

        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        // var flags = pFlags.GetGetter<ComputedFlags>().Invoke(computed);
        var flags = (ComputedFlags)pFlags.GetMethod!.Invoke(computed, Array.Empty<object?>())!;
        sb.Append("Computed: ").Append(computed).AppendLine();
        sb.Append("- Flags: ").Append(flags).AppendLine();
        sb.AppendLine("- Dependencies:");
        var impl = (IComputedImpl)computed;
        foreach (var d in impl.Used)
            sb.Append("  - ").Append(d).AppendLine();
        return sb.ToStringAndRelease();
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
