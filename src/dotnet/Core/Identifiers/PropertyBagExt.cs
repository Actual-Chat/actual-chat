namespace ActualChat;

public static class PropertyBagExt
{
    public static T GetId<T>(this PropertyBag properties, T @default = default!)
        where T: struct, ISymbolIdentifier<T>
    {
        var value = properties[typeof(T)];
        return value switch {
            null => @default,
            string sValue => T.ParseOrNone(sValue),
            T tValue => tValue,
            _ => (T)value,
        };
    }

    public static T GetId<T>(this MutablePropertyBag properties, T @default = default!)
        where T: struct, ISymbolIdentifier<T>
    {
        var value = properties[typeof(T)];
        return value switch {
            null => @default,
            string sValue => T.ParseOrNone(sValue),
            T tValue => tValue,
            _ => (T)value,
        };
    }

    public static MutablePropertyBag SetId<T>(this MutablePropertyBag properties, T value)
        where T: struct, ISymbolIdentifier<T>
    {
        properties.Set(value);
        return properties;
    }
}
