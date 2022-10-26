using Stl.Reflection;

namespace ActualChat.UI.Blazor.Components;

public static class MenuRegistry
{
    private static readonly ConcurrentDictionary<Type, Symbol> TypeToTypeId = new();
    private static readonly ConcurrentDictionary<Symbol, Type> TypeIdToType = new();

    public static string GetTypeId<TMenu>()
        where TMenu: MenuBase
        => TypeToTypeId.GetOrAdd(typeof(TMenu), type1 => {
            var typeId = type1.ToSymbol(false);
            TypeIdToType.GetOrAdd(typeId, type1);
            return typeId;
        });

    public static Type GetType(string typeId)
        => TypeIdToType.GetValueOrDefault(typeId)
            ?? throw new KeyNotFoundException(typeId);
}
