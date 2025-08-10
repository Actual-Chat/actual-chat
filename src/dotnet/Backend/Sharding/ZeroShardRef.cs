namespace ActualChat.Sharding;

#pragma warning disable CS0169

// This type is used as the first parameter of RPC methods requiring routing to a random shard
public interface IRequiresZeroShard;

// This type is used as the first parameter of RPC methods requiring routing to a random shard
[StructLayout(LayoutKind.Sequential, Pack = 1)] // Important!
public readonly struct ZeroShardRef : IEquatable<ZeroShardRef>
{
    public static readonly ZeroShardRef Value = default!;

    // See https://github.com/dotnet/runtime/pull/107198
    [Obsolete("This member exists solely to make Mono AOT work. Don't use it!")]
    private readonly byte _dummyValue;

    // Equality
    public bool Equals(ZeroShardRef other) => true;
    public override bool Equals(object? obj) => obj is ZeroShardRef;
    public override int GetHashCode() => 0;
}
