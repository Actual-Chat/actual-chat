using ActualChat.Hashing;

namespace ActualChat.Mesh;

[DebuggerDisplay("{" + nameof(DebugValue) + "}")]
public sealed record MeshNode(
    NodeRef Ref,
    string Endpoint,
    IReadOnlySet<HostRole> Roles,
    MeshNodeState State
    ) : IComparable<MeshNode>, IHasId<NodeRef>, IHasId<Symbol>
{
    private static readonly ListFormat Formatter = new(' ');

    private string? _toStringCached;

    NodeRef IHasId<NodeRef>.Id => Ref;
    Symbol IHasId<Symbol>.Id => Ref.Id;

    private string DebugValue => ToString();

    public string LockKey
        => field ??= Formatter.Format(Ref.Value, Endpoint, Roles.ToDelimitedString(","));

    public static MeshNode FromLockKey(string lockKey)
        => TryParseLockKey(lockKey, out var result)
            ? result
            : throw StandardError.Format<MeshNode>();

    public override string ToString()
        => _toStringCached ??= $"{LockKey}: {State}";

    // Helpers

    public MeshNode ToDead()
        => State is not MeshNodeState.Online
            ? throw StandardError.StateTransition<MeshNode>($"Expected {nameof(State.Online)} state, but got {State}.")
            : this with {
                State = MeshNodeState.Dead,
            };

    // Equality: uses only Ref

    public bool Equals(MeshNode? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        return ReferenceEquals(this, other) || Equals(Ref.Id, other.Ref.Id);
    }

    public override int GetHashCode() => Ref.Id.HashCode;

    public IEnumerable<T> GetHashes<T>()
        where T : unmanaged
    {
        for (var hashSource = Ref.Value;; hashSource += Ref.Value) {
            var hashes = hashSource.Hash().Blake2b().AsSpan<T>().ToArray();
            foreach (var hash in hashes)
                yield return hash;
        }
    }

    // Comparison: uses only Ref

    public int CompareTo(MeshNode? other)
        => ReferenceEquals(other, null)
            ? 1
            : string.Compare(Ref.Value, other.Ref.Value);

    public static bool operator <(MeshNode left, MeshNode right)
        => ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
    public static bool operator <=(MeshNode left, MeshNode right)
        => ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
    public static bool operator >(MeshNode left, MeshNode right)
        => !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
    public static bool operator >=(MeshNode left, MeshNode right)
        => ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;

    // Private methods

    private static bool TryParseLockKey(string value, [NotNullWhen(true)] out MeshNode? nodeInfo)
    {
        nodeInfo = null;
        var parser = Formatter.CreateParser(value);

        // Id
        if (!parser.TryParseNext())
            return false;
        if (!NodeRef.TryParse(parser.Item, out var @ref))
            return false;

        // Endpoint
        if (!parser.TryParseNext())
            return false;
        var endpoint = parser.Item;
        if (endpoint.IsNullOrEmpty())
            return false;

        // Roles
        var roles = Enumerable.Empty<HostRole>();
        if (parser.TryParseNext()) {
            roles = parser.Item.Split(',').Select(x => (HostRole)x);
            if (parser.TryParseNext())
                return false;
        }

        nodeInfo = new MeshNode(@ref, endpoint, new HashSet<HostRole>(roles), MeshNodeState.Online);
        return true;
    }
}
