using System.Collections.Frozen;

namespace ActualChat.Serialization.Internal;

public sealed record AppMessagePackResolverAssemblyPlan(
    FrozenDictionary<Type, Type>? Formatters,
    IFormatterResolver[] Resolvers)
{
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "We assume MessagePack formatters are preserved")]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "We assume MessagePack formatters are preserved")]
    [UnconditionalSuppressMessage("Trimming", "IL3050", Justification = "We assume MessagePack formatters are preserved")]
    public object? TryGetFormatter(Type type)
    {
        if (Formatters is null)
            return null;

        Type? formatterType;
        if (!type.IsGenericType) {
            return Formatters.TryGetValue(type, out formatterType)
                ? formatterType.CreateInstance()
                : null;
        }

        var gtd = type.GetGenericTypeDefinition();
        return Formatters.TryGetValue(gtd, out formatterType)
            ? formatterType.MakeGenericType(type.GetGenericArguments()).CreateInstance()
            : null;
    }
}
