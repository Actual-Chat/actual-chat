namespace ActualChat.UI.Blazor.Components;

public static class MenuRegistry
{
    private static readonly ConcurrentDictionary<Type, string> TypeToId = new();
    private static readonly ConcurrentDictionary<string, Type> IdToType = new(StringComparer.Ordinal);
    private static int _lastMenuId;

    public static string GetTypeId([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => TypeToId.GetOrAdd(type, static type1 => {
            if (!type1.IsAssignableTo(typeof(IMenu)))
                throw new ArgumentOutOfRangeException(nameof(type));

            var menuId = Interlocked.Increment(ref _lastMenuId);
            var typeId = $"{type1.Name}-{menuId}";
            IdToType.GetOrAdd(typeId, type1);
            return typeId;
        });

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2073:ReturnValueDoesNotMatchAnnotation",
        Justification = "All possible results already have annotation.")]
    public static Type GetType(string typeId)
        => IdToType.GetValueOrDefault(typeId)
            ?? throw new KeyNotFoundException(typeId);
}
