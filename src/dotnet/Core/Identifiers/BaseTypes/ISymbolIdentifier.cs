namespace ActualChat;

public interface ISymbolIdentifier : IHasId<Symbol>, ICanBeNone
{
    string Value { get; }
}

#pragma warning disable CA1000

public interface ISymbolIdentifier<TSelf> : ISymbolIdentifier, IEquatable<TSelf>, ICanBeNone<TSelf>
    where TSelf : ISymbolIdentifier<TSelf>
{
    static abstract TSelf Parse(string? s);
    static abstract TSelf ParseOrNone(string s);
    static abstract bool TryParse(string? s, out TSelf result);
}
