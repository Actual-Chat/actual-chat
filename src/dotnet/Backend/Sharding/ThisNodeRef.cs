namespace ActualChat.Sharding;

#pragma warning disable CS0169 // Field is never used

// This type is used as the first parameter of RPC methods requiring routing to the local node
public interface IRequiresThisNode;

// This type is used as the first parameter of RPC methods requiring routing to the local node
[StructLayout(LayoutKind.Sequential, Pack = 1)] // Important!
public readonly struct ThisNodeRef : IEquatable<ThisNodeRef>
{
    public static readonly ThisNodeRef Value = default!;

    // See https://github.com/dotnet/runtime/pull/107198
    [Obsolete("This member exists solely to make Mono AOT work. Don't use it!")]
    private readonly byte _dummyValue;

    // Equality
    public bool Equals(ThisNodeRef other) => true;
    public override bool Equals(object? obj) => obj is ThisNodeRef;
    public override int GetHashCode() => 0;
}
