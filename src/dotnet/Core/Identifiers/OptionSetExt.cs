namespace ActualChat;

public static class OptionSetExt
{
    public static T GetId<T>(this OptionSet optionSet, T @default = default!)
        where T: struct, ISymbolIdentifier<T>
    {
        var value = optionSet[typeof(T)];
        return value switch {
            null => @default,
            string sValue => T.ParseOrNone(sValue),
            T tValue => tValue,
            _ => (T)value,
        };
    }

    public static OptionSet SetId<T>(this OptionSet optionSet, T value)
        where T: struct, ISymbolIdentifier<T>
    {
        optionSet.Set(value);
        return optionSet;
    }
}
