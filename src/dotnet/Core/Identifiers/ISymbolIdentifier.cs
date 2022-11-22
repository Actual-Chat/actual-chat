namespace ActualChat;

public interface ISymbolIdentifier : IHasId<Symbol>, IRequirementTarget, ICanBeEmpty
{
    string Value { get; }
}

public interface ISymbolIdentifier<TSelf> : ISymbolIdentifier, IEquatable<TSelf>
    where TSelf : ISymbolIdentifier<TSelf>
{
    static abstract TSelf Parse(string? s);
    static abstract TSelf ParseOrDefault(string s);
    static abstract bool TryParse(string? s, out TSelf result);
}
