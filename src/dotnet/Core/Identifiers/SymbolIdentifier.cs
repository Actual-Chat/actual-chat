namespace ActualChat;

/// <summary>
/// Base interface for Symbol-based identifiers.
/// </summary>
#pragma warning disable CA1000, CA2252
public interface ISymbolIdentifier : IStringLike, IHasId<Symbol>, ICanBeNone
{
}

/// <summary>
/// Generic interface for Symbol-based identifiers with parsing support.
/// </summary>
public interface ISymbolIdentifier<TSelf> : ISymbolIdentifier, IStringLike<TSelf>,
    IEquatable<TSelf>, IComparable<TSelf>, ICanBeNone<TSelf>
    where TSelf : struct, ISymbolIdentifier<TSelf>
{
    // Parse(string?) is inherited from IStringLike<TSelf>.
    static abstract TSelf ParseOrNone(string? s);
    static abstract bool TryParse(string? s, out TSelf result);

    int IComparable<TSelf>.CompareTo(TSelf other)
        => string.CompareOrdinal(Id.Value, other.Id.Value);
}

/// <summary>
/// Helper methods for parsing <see cref="ISymbolIdentifier{TSelf}"/> types.
/// </summary>
public static class SymbolIdentifier
{
    public static T Parse<T>(string? s)
        where T : struct, ISymbolIdentifier<T>
        => T.Parse(s);

    public static T ParseOrNone<T>(string? s)
        where T : struct, ISymbolIdentifier<T>
        => T.ParseOrNone(s);
}
