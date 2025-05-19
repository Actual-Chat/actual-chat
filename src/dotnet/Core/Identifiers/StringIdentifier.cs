using System.Numerics;
using ActualLab.Rpc;

namespace ActualChat;

// ReSharper disable once PossibleInterfaceMemberAmbiguity
public interface IStringIdentifier : IHasId<string>, IHasId<Symbol>
{
    public string Value { get; }
    public int HashCode { get; }
}

public interface IStringIdentifier<TSelf> : IStringIdentifier,
    IEquatable<TSelf>, IComparable<TSelf>, IEqualityOperators<TSelf, TSelf, bool>
    where TSelf : StringIdentifier, IStringIdentifier<TSelf>
{
    public static abstract TSelf Parse(string s); // Must rely on TryParse(s, out result)
    public static abstract TSelf? ParseNullable(string? s); // Must rely on Parse(s)
    public static abstract TSelf? TryParse(string? s, bool allowNull = false); // Must rely on TryParse(s, out result)
    public static abstract bool TryParse(string? s, [NotNullWhen(true)] out TSelf? result);

    int IComparable<TSelf>.CompareTo(TSelf? other)
        => string.CompareOrdinal(Value, other?.Value);
}

public abstract class StringIdentifier(string value) : IStringIdentifier
{
    [DataMember(Order = 0)]
    public readonly string Value = value;
    [IgnoreDataMember]
    public readonly int HashCode = value.GetHashCode(StringComparison.Ordinal);
    [IgnoreDataMember]
    public Symbol Id => new(Value, HashCode);

    // IStringIdentifier members
    string IHasId<string>.Id => Value;
    string IStringIdentifier.Value => Value;
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
        => RpcDefaults.Mode == RpcMode.Server
            ? new ConcurrentLruCache<string, TSelf>(
                clientSideCapacity * serverSideCapacityMultiplier,
                serverSideCacheCount,
                StringComparer.Ordinal)
            : new ThreadSafeLruCache<string, TSelf>(clientSideCapacity);
}
