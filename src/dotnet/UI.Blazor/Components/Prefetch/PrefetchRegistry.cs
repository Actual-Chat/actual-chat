namespace ActualChat.UI.Blazor.Components;

public static class PrefetchRegistry
{
    private static readonly ConcurrentDictionary<Type, string> TypeToId = new();
    private static readonly ConcurrentDictionary<string, Type> IdToType = new();
    private static int _lastPrefetcherId;

    public static string GetTypeId([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        => TypeToId.GetOrAdd(type, static type1 => {
            if (!type1.IsAssignableTo(typeof(IPrefetcher)))
                throw new ArgumentOutOfRangeException(nameof(type));

            var prefetcherId = Interlocked.Increment(ref _lastPrefetcherId);
            var typeId = $"{type1.Name}-{prefetcherId}";
            IdToType.GetOrAdd(typeId, type1);
            return typeId;
        });

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2073:ReturnValueDoesNotMatchAnnotation",
        Justification = "All possible results already have annotation.")]
    public static Type GetType(string typeId)
        => TryGetType(typeId)
            ?? throw new KeyNotFoundException(typeId);

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2073:ReturnValueDoesNotMatchAnnotation",
        Justification = "All possible results already have annotation.")]
    public static Type? TryGetType(string typeId)
        // An unregistered id means "no prefetcher" - markup can outlive the build that rendered it
        => IdToType.GetValueOrDefault(typeId);
}
