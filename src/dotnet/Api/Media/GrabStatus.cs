using System.Text;
using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Media;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record GrabStatus(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0)
    : IHasId<Symbol>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public bool Success { get; init; }
    [DataMember, MemoryPackOrder(3)] public Moment ModifiedAt { get; init; }

    // Computed properties

    public GrabStatus() : this(MediaId.None) { }

    // This record relies on referential equality
    public bool Equals(GrabStatus? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public static Symbol ComposeId(string url)
        => url.IsNullOrEmpty()
            ? Symbol.Empty
            : url.Hash(Encoding.UTF8).SHA256().AlphaNumeric();
}
