namespace ActualChat;

#pragma warning disable CA1000, CA2252

public interface ISymbolIdentifier : IHasId<Symbol>, ICanBeNone
{
    public string Value { get; }
}

public interface ISymbolIdentifier<TSelf> : ISymbolIdentifier, IEquatable<TSelf>, IComparable<TSelf>, ICanBeNone<TSelf>
    where TSelf : struct, ISymbolIdentifier<TSelf>
{
    public static abstract TSelf Parse(string? s);
    public static abstract TSelf ParseOrNone(string? s);
    public static abstract bool TryParse(string? s, out TSelf result);

    int IComparable<TSelf>.CompareTo(TSelf other)
        => string.CompareOrdinal(Id.Value, other.Id.Value);
}

public static class SymbolIdentifier
{
    public static T Parse<T>(string? s)
        where T : struct, ISymbolIdentifier<T>
        => T.Parse(s);

    public static T ParseOrNone<T>(string? s)
        where T : struct, ISymbolIdentifier<T>
        => T.ParseOrNone(s);
}
