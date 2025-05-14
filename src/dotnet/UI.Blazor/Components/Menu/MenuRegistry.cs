namespace ActualChat.UI.Blazor.Components;

public static class MenuRegistry
{
    private static readonly ConcurrentDictionary<Type, string> TypeToTypeId = new();
    private static readonly ConcurrentDictionary<string, Type> TypeIdToType = new(StringComparer.Ordinal);
    private static int _lastMenuId;

    public static string GetTypeId<TMenu>()
        where TMenu : IMenu
        => GetTypeId(typeof(TMenu));

    public static string GetTypeId(Type type)
        => TypeToTypeId.GetOrAdd(type, type1 => {
            if (!type1.IsAssignableTo(typeof(IMenu)))
                throw new ArgumentOutOfRangeException(nameof(type));

            var menuId = Interlocked.Increment(ref _lastMenuId);
            var typeId = new Symbol($"{type1.Name}-{menuId}");
            TypeIdToType.GetOrAdd(typeId, type1);
            return typeId;
        });

    public static Type GetType(Symbol typeId)
        => TypeIdToType.GetValueOrDefault(typeId)
            ?? throw new KeyNotFoundException(typeId);
}
