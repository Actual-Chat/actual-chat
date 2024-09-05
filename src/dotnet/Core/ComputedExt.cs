using ActualLab.Fusion.Internal;
using ActualLab.Fusion.Operations.Internal;

namespace ActualChat;

public static class ComputedExt
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo?> FieldCache = new();

    // Debug dump

    public static string DebugDump(this Computed computed, int maxDepth = 0)
    {
        var type = computed.GetType();
        var pFlags = GetProperty(type, "Flags")!;

        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        // var flags = pFlags.GetGetter<ComputedFlags>().Invoke(computed);
        var flags = (ComputedFlags)pFlags.GetMethod!.Invoke(computed, [])!;
        sb.Append("Computed: ").Append(computed).AppendLine();
        sb.Append("- Flags: ").Append(flags).AppendLine();
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
