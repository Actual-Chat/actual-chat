namespace ActualChat.UI.Blazor.Components;

public static class BubbleRegistry
{
    private static readonly ConcurrentDictionary<Type, Symbol> TypeToTypeId = new();
    private static readonly ConcurrentDictionary<Symbol, Type> TypeIdToType = new();

    public static Symbol GetTypeId(Type type)
        => TypeToTypeId.GetOrAdd(type, type1 => {
            if (!type1.IsAssignableTo(typeof(IBubble)))
                throw new ArgumentOutOfRangeException(nameof(type));

            // NOTE(AY): We intentionally use just type name here -
            // to make sure we can move them across namespaces w/o losing
            // read status.
            var typeId = new Symbol(type1.Name);
            TypeIdToType.GetOrAdd(typeId, type1);
            return typeId;
        });

    public static Type GetType(Symbol typeId)
        => TypeIdToType.GetValueOrDefault(typeId)
            ?? throw new KeyNotFoundException(typeId);
}
