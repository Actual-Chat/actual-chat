using System.Numerics;

namespace ActualChat;

/// <summary>
/// Base interface for string-based identifiers with hash code caching.
/// </summary>
// ReSharper disable once PossibleInterfaceMemberAmbiguity
public interface IStringIdentifier : IStringLike, IHasId<string>, IHasId<Symbol>
{
    int HashCode { get; }
}

/// <summary>
/// Generic interface for string-based identifiers with parsing support.
/// </summary>
public interface IStringIdentifier<TSelf> : IStringIdentifier, IStringLike<TSelf>,
    IEquatable<TSelf>, IComparable<TSelf>, IEqualityOperators<TSelf, TSelf, bool>
    where TSelf : StringIdentifier, IStringIdentifier<TSelf>
{
    // Parse(string?) is inherited from IStringLike<TSelf>; existing Parse(string s) implementations satisfy it at IL level.
    static abstract TSelf? ParseNullable(string? s); // Must rely on Parse(s)
    static abstract TSelf? TryParse(string? s, bool allowNull = false); // Must rely on TryParse(s, out result)
    static abstract bool TryParse(string? s, [NotNullWhen(true)] out TSelf? result);

    int IComparable<TSelf>.CompareTo(TSelf? other)
        => string.CompareOrdinal(Value, other?.Value);
}

/// <summary>
/// Base class for string-based identifiers with cached hash code.
/// </summary>
public abstract class StringIdentifier(string value) : IStringIdentifier
{
    [DataMember(Order = 0)]
    public readonly string Value = value;
    [IgnoreDataMember]
    public readonly int HashCode = value.GetHashCode();
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public Symbol Id => new(Value, HashCode);

    // IStringIdentifier members
    string IHasId<string>.Id => Value;
    string IStringLike.Value => Value;
    int IStringIdentifier.HashCode => HashCode;

    public override string ToString()
        => Value;

    public override int GetHashCode()
        => HashCode;

    // Protected methods

    protected static ILruCache<string, TSelf> CreateCache<TSelf>(
        int clientSideCapacity,
        int serverSideCapacityMultiplier = 16, // That's per cache
        int serverSideCacheCount = 0)
        => RuntimeInfo.IsServer
            ? new ConcurrentLruCache<string, TSelf>(
                clientSideCapacity * serverSideCapacityMultiplier,
                serverSideCacheCount,
                StringComparer.Ordinal)
            : new ThreadSafeLruCache<string, TSelf>(clientSideCapacity);
}
