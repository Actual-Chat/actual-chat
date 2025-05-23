namespace ActualChat.UI.Blazor.Components;

public static class BubbleRegistry
{
    private static readonly ConcurrentDictionary<Type, string> TypeToId = new();
    private static readonly ConcurrentDictionary<string, Type> IdToType = new(StringComparer.Ordinal);

    public static string GetTypeId([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => TypeToId.GetOrAdd(type, static type1 => {
            if (!type1.IsAssignableTo(typeof(IBubble)))
                throw new ArgumentOutOfRangeException(nameof(type));

            // NOTE(AY): We intentionally use just type name here -
            // to make sure we can move them across namespaces w/o losing
            // read status.
            var typeId = type1.Name;
            IdToType.GetOrAdd(typeId, type1);
            return typeId;
        });

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2073:ReturnValueDoesNotMatchAnnotation",
        Justification = "All possible results already have annotation.")]
    public static Type GetType(Symbol typeId)
        => IdToType.GetValueOrDefault(typeId)
            ?? throw new KeyNotFoundException(typeId);
}
