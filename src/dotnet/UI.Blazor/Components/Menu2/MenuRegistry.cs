using Stl.Reflection;

namespace ActualChat.UI.Blazor.Components;

public static class MenuRegistry
{
    private static readonly ConcurrentDictionary<Type, Symbol> TypeToTypeId = new();
    private static readonly ConcurrentDictionary<Symbol, Type> TypeIdToType = new();

    public static string GetTypeId<TMenu>()
        where TMenu: MenuBase
        => GetTypeId(typeof(TMenu));

    public static Symbol GetTypeId(Type type)
        => TypeToTypeId.GetOrAdd(type, type1 => {
            if (!type1.IsAssignableTo(typeof(MenuBase)))
                throw new ArgumentOutOfRangeException(nameof(type));
            var typeId = type1.ToSymbol(false);
            TypeIdToType.GetOrAdd(typeId, type1);
            return typeId;
        });

    public static Type GetType(Symbol typeId)
        => TypeIdToType.GetValueOrDefault(typeId)
            ?? throw new KeyNotFoundException(typeId);
}
